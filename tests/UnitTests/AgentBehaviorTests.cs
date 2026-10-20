using System.Collections.Concurrent;
using System.Reflection;
using Aiursoft.MarkToHtml.Services.Agent;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class AgentBehaviorTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("max_tokens")]
    [DataRow("refusal")]
    public async Task TextResponse_CompletesWithoutInterpretingStopReason(string? stopReason)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(),
            _ => new ClaudeResponse
            {
                StopReason = stopReason,
                ReasoningContent = "private reasoning",
                Content = [ClaudeContentBlock.TextBlock("First"), ClaudeContentBlock.TextBlock("Second")]
            });
        var (service, conversation, _) = CreateService(services, client);

        await Run(service, services, conversation.Id);

        Assert.AreEqual(AgentState.Completed, conversation.State);
        Assert.AreEqual(1, conversation.LoopCount);
        Assert.AreEqual("First\nSecond", conversation.Messages[^1].Content);
        Assert.AreEqual("private reasoning", conversation.Messages[^1].ReasoningContent);
        Assert.AreEqual("Original document", conversation.DocumentContent);
        Assert.AreEqual(1, client.Requests.Count);
        Assert.AreEqual("Original system", client.Requests[0].System);
        Assert.AreEqual("Question", client.Requests[0].Messages.Single().Content);
    }

    [TestMethod]
    public async Task EmptyResponse_CompletesWithoutAddingAssistantMessage()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), _ => new ClaudeResponse());
        var (service, conversation, _) = CreateService(services, client);
        var count = conversation.Messages.Count;

        await Run(service, services, conversation.Id);

        Assert.AreEqual(AgentState.Completed, conversation.State);
        Assert.AreEqual(count, conversation.Messages.Count);
        Assert.AreEqual(1, client.Requests.Count);
    }

    [TestMethod]
    public async Task UnknownReadTool_ReturnsErrorAndContinuesWithReasoningAndCallIdentity()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), index => index == 0
            ? new ClaudeResponse
            {
                ReasoningContent = "reasoning to preserve",
                Content = [ClaudeContentBlock.ToolUse("read-1", "MissingTool", new())]
            }
            : new ClaudeResponse { Content = [ClaudeContentBlock.TextBlock("Recovered")] });
        var (service, conversation, _) = CreateService(services, client);

        await Run(service, services, conversation.Id);

        Assert.AreEqual(AgentState.Completed, conversation.State);
        Assert.AreEqual(2, client.Requests.Count);
        var assistant = client.Requests[1].Messages[1];
        Assert.AreEqual("reasoning to preserve", assistant.ReasoningContent);
        var call = ((List<ClaudeContentBlock>)assistant.Content).Single();
        Assert.AreEqual("read-1", call.Id);
        Assert.AreEqual("MissingTool", call.Name);
        var result = ((List<ClaudeContentBlock>)client.Requests[1].Messages[2].Content).Single();
        Assert.AreEqual("read-1", result.ToolUseId);
        StringAssert.Contains(result.Content!.ToString()!, "MissingTool");
        Assert.AreEqual("Recovered", conversation.Messages[^1].Content);
    }

    [TestMethod]
    public async Task WriteProposals_PauseWithoutExecutingAndPreserveReadResults()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), _ => new ClaudeResponse
        {
            Content =
            [
                ClaudeContentBlock.ToolUse("write-1", "ReplaceText", new() { ["oldString"] = "Original", ["newString"] = "Edited" }),
                ClaudeContentBlock.ToolUse("read-1", "MissingTool", new()),
                ClaudeContentBlock.ToolUse("write-2", "ReplaceText", new() { ["oldString"] = "document", ["newString"] = "text" })
            ]
        });
        var (service, conversation, advice) = CreateService(services, client);

        await Run(service, services, conversation.Id);

        Assert.AreEqual(AgentState.AwaitingApproval, conversation.State);
        Assert.AreEqual(1, client.Requests.Count);
        Assert.AreEqual("Original document", conversation.DocumentContent);
        Assert.IsFalse(conversation.DocumentContentUpdated);
        Assert.AreEqual(2, conversation.PendingAdviceIds.Count);
        var pending = advice.GetPendingForConversation(conversation.Id);
        CollectionAssert.AreEquivalent(new[] { "write-1", "write-2" }, pending.Select(item => item.ToolCallId).ToArray());
        Assert.IsTrue(pending.All(item => item.Status == AdviceStatus.Pending));
        Assert.IsTrue(pending.All(item => item.DocumentContentSnapshot == "Original document"));
        var result = conversation.Messages.Single(item => item.Role == "tool");
        Assert.AreEqual("read-1", result.ToolCallId);
    }

    [TestMethod]
    public async Task TenToolRounds_CompleteWithExistingLimitMessage()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), index => new ClaudeResponse
        {
            Content = [ClaudeContentBlock.ToolUse($"read-{index}", "MissingTool", new())]
        });
        var (service, conversation, _) = CreateService(services, client);

        await Run(service, services, conversation.Id);

        Assert.AreEqual(10, client.Requests.Count);
        Assert.AreEqual(10, conversation.LoopCount);
        Assert.AreEqual(AgentState.Completed, conversation.State);
        Assert.AreEqual("I've reached the maximum number of steps. Please refine your request or approve pending actions.",
            conversation.Messages[^1].Content);
    }

    [TestMethod]
    public async Task ModelException_PreservesExistingErrorStateAndMessage()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(),
            _ => throw new InvalidOperationException("fixture failure"));
        var (service, conversation, _) = CreateService(services, client);

        await Run(service, services, conversation.Id);

        Assert.AreEqual(AgentState.Error, conversation.State);
        Assert.AreEqual("fixture failure", conversation.ErrorMessage);
        Assert.AreEqual("Original document", conversation.DocumentContent);
    }

    [TestMethod]
    public async Task CancelRun_RemovesConversationAndAdviceWithoutCallingModel()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), _ => new ClaudeResponse());
        var (service, conversation, advice) = CreateService(services, client);
        var proposal = advice.Create(conversation.Id, "ReplaceText", "Replace", "Edit", new(), "Edit");

        service.CancelRun(conversation.Id);
        await Run(service, services, conversation.Id);

        Assert.IsNull(service.GetConversation(conversation.Id));
        Assert.IsNull(advice.Get(proposal.Id));
        Assert.AreEqual(0, client.Requests.Count);
    }

    private static (AgentService Service, AgentConversation Conversation, AdviceService Advice) CreateService(
        IServiceProvider services, ClaudeClient client)
    {
        var advice = new AdviceService();
        var service = new AgentService(null!, new ToolRegistry(services), advice, client,
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentService>.Instance);
        var conversation = new AgentConversation
        {
            UserId = "owner",
            DocumentContent = "Original document",
            Messages =
            [
                new ToolMessagesItem { Role = "system", Content = "Original system" },
                new ToolMessagesItem { Role = "user", Content = "Question" }
            ]
        };
        var field = typeof(AgentService).GetField("_conversations", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((ConcurrentDictionary<Guid, AgentConversation>)field.GetValue(service)!)[conversation.Id] = conversation;
        return (service, conversation, advice);
    }

    private static Task Run(AgentService service, IServiceProvider services, Guid id) =>
        (Task)typeof(AgentService).GetMethod("ExecuteReActLoop", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [services, id])!;

    private sealed class ScriptedClient(IServiceScopeFactory scopes, Func<int, ClaudeResponse> respond)
        : ClaudeClient(scopes, NullLogger<ClaudeClient>.Instance)
    {
        public List<ClaudeRequest> Requests { get; } = [];

        public override Task<ClaudeResponse> SendAsync(string systemPrompt, List<ClaudeMessage> messages,
            List<ClaudeTool>? tools, CancellationToken ct = default, int maxTokens = 4096)
        {
            var request = new ClaudeRequest { System = systemPrompt, Messages = messages, Tools = tools, MaxTokens = maxTokens };
            Requests.Add(request);
            return Task.FromResult(respond(Requests.Count - 1));
        }
    }
}
