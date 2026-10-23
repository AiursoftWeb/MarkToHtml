using System.Collections.Concurrent;
using System.Reflection;
using Aiursoft.Canon.TaskQueue;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.InMemory;
using Aiursoft.MarkToHtml.Services.Agent;
using Aiursoft.MarkToHtml.Services.Tools;
using Microsoft.EntityFrameworkCore;
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

    [TestMethod]
    [DataRow("ReadFullDocument", 1, 3, "1|First\n2|Second\n3|Third")]
    [DataRow("ReadDocumentLines", 2, 2, "2|Second")]
    [DataRow("ReadDocumentLines", -5, 99, "1|First\n2|Second\n3|Third")]
    [DataRow("ReadDocumentLines", 99, -5, "3|Third")]
    public async Task RealReadTools_UseConversationContentAndPreserveCallIdentity(
        string toolName, int startLine, int endLine, string expected)
    {
        using var services = CreateToolServices();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), index => index == 0
            ? new ClaudeResponse
            {
                Content = [ClaudeContentBlock.ToolUse("real-read", toolName,
                    toolName == "ReadFullDocument" ? new() : new() { ["startLine"] = startLine, ["endLine"] = endLine })]
            }
            : new ClaudeResponse { Content = [ClaudeContentBlock.TextBlock("Read complete")] });
        var (service, conversation, _) = CreateService(services, client);
        conversation.DocumentContent = "First\nSecond\nThird";

        await Run(service, services, conversation.Id);

        Assert.AreEqual(AgentState.Completed, conversation.State);
        var result = ((List<ClaudeContentBlock>)client.Requests[1].Messages[^1].Content).Single();
        Assert.AreEqual("real-read", result.ToolUseId);
        Assert.AreEqual(expected.Replace("\n", Environment.NewLine), result.Content);
        Assert.AreEqual("First\nSecond\nThird", conversation.DocumentContent);
        Assert.IsFalse(conversation.DocumentContentUpdated);
        // Tool execution must use a fresh scope, not mutate the caller's context.
        Assert.AreEqual(string.Empty, services.GetRequiredService<CurrentUserService>().DocumentContent);
    }

    [TestMethod]
    public async Task RealReadTool_InvalidArgumentsReturnResultAndContinue()
    {
        using var services = CreateToolServices();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), index => index == 0
            ? new ClaudeResponse
            {
                Content = [ClaudeContentBlock.ToolUse("invalid-read", "ReadDocumentLines", new() { ["startLine"] = "not-a-number" })]
            }
            : new ClaudeResponse { Content = [ClaudeContentBlock.TextBlock("Recovered")] });
        var (service, conversation, _) = CreateService(services, client);

        await Run(service, services, conversation.Id);

        Assert.AreEqual(AgentState.Completed, conversation.State);
        var result = conversation.Messages.Single(message => message.Role == "tool");
        Assert.AreEqual("invalid-read", result.ToolCallId);
        StringAssert.Contains(result.Content!, "Error");
        Assert.AreEqual("Recovered", conversation.Messages[^1].Content);
        Assert.AreEqual(2, client.Requests.Count);
    }

    [TestMethod]
    public async Task ApprovedReplaceText_PersistsAndExposesUpdatedContentBeforeResume()
    {
        using var services = CreateToolServices();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), index => index switch
        {
            0 => new ClaudeResponse { Content = [Replacement("write-1", "Original", "Edited")] },
            1 => new ClaudeResponse { Content = [ClaudeContentBlock.ToolUse("verify", "ReadFullDocument", new())] },
            _ => new ClaudeResponse { Content = [ClaudeContentBlock.TextBlock("Verified")] }
        });
        var (service, conversation, advice) = CreateService(services, client);
        var previousUpdate = DateTime.UtcNow.AddDays(-1);
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = conversation.DocumentId, UserId = conversation.UserId,
                Content = conversation.DocumentContent, UpdatedAt = previousUpdate
            });
            await db.SaveChangesAsync();
        }
        await Run(service, services, conversation.Id);
        var proposal = advice.GetPendingForConversation(conversation.Id).Single();

        service.ApproveAdvice(conversation.Id, proposal.Id);
        Assert.AreEqual(AdviceStatus.Approved, proposal.Status);
        Assert.AreEqual("Original document", conversation.DocumentContent);
        Assert.AreEqual($"AdviceExecute-{proposal.Id}", services.GetRequiredService<ServiceTaskQueue>().GetPendingTasks().Single().TaskName);
        await ExecuteApproved(service, services, conversation.Id, proposal.Id);

        Assert.AreEqual(AgentState.Completed, conversation.State);
        Assert.AreEqual(3, client.Requests.Count);
        Assert.AreEqual("Verified", conversation.Messages[^1].Content);
        Assert.AreEqual("Edited document", conversation.DocumentContent);
        Assert.AreEqual("Edited document", service.GetUpdatedDocumentContent(conversation.Id));
        Assert.IsTrue(conversation.DocumentContentUpdated);
        Assert.AreEqual(0, conversation.PendingAdviceIds.Count);
        Assert.AreEqual(AdviceStatus.Executed, proposal.Status);
        Assert.IsNull(proposal.Error);
        StringAssert.Contains(proposal.Result!, "Document updated successfully");
        Assert.AreEqual("write-1", conversation.Messages.First(message => message.Role == "tool").ToolCallId);
        Assert.IsTrue(conversation.Messages.Any(message => message.Role == "tool" && message.Content == "1|Edited document"));
        using var verification = services.CreateScope();
        var stored = await verification.ServiceProvider.GetRequiredService<TemplateDbContext>()
            .MarkdownDocuments.AsNoTracking().SingleAsync();
        Assert.AreEqual("Edited document", stored.Content);
        Assert.IsTrue(stored.UpdatedAt > previousUpdate);
    }

    [TestMethod]
    public async Task ContinueRun_ResetsTenRoundBudgetAndAppendsSnapshotWithoutRewritingHistory()
    {
        using var services = CreateToolServices();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), index => new ClaudeResponse
        {
            Content = [ClaudeContentBlock.ToolUse($"read-{index}", "ReadFullDocument", new())]
        });
        var (service, conversation, _) = CreateService(services, client);
        await Run(service, services, conversation.Id);
        var history = conversation.Messages.Select(message => (message.Role, message.Content, message.ToolCallId)).ToArray();
        conversation.DocumentContent = "Updated snapshot\nSecond line";

        Assert.AreEqual(conversation.Id, service.ContinueRun(conversation.Id, "owner", "Continue"));
        Assert.AreEqual(0, conversation.LoopCount);
        Assert.AreEqual(AgentState.Thinking, conversation.State);
        Assert.AreEqual(history.Length + 3, conversation.Messages.Count);
        CollectionAssert.AreEqual(history, conversation.Messages.Take(history.Length)
            .Select(message => (message.Role, message.Content, message.ToolCallId)).ToArray());
        Assert.IsTrue(conversation.Messages[^3].IsMeta);
        Assert.IsTrue(conversation.Messages[^2].IsMeta);
        Assert.AreEqual($"<document-content>\n1|Updated snapshot{Environment.NewLine}2|Second line\n</document-content>",
            conversation.Messages[^2].Content);
        Assert.AreEqual("Continue", conversation.Messages[^1].Content);
        Assert.AreEqual($"AgentContinue-{conversation.Id}", services.GetRequiredService<ServiceTaskQueue>().GetPendingTasks().Single().TaskName);
        await Run(service, services, conversation.Id);

        Assert.AreEqual(20, client.Requests.Count);
        Assert.AreEqual(10, conversation.LoopCount);
        Assert.AreEqual(AgentState.Completed, conversation.State);
        Assert.AreEqual("Question", client.Requests[0].Messages.Single().Content);
        Assert.AreEqual("Continue", client.Requests[10].Messages[^1].Content);
    }

    [TestMethod]
    public async Task MultipleProposals_FirstApprovalResumesModelDespiteOtherPendingAdvice_LegacyBehavior()
    {
        using var services = CreateToolServices();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), index => index == 0
            ? new ClaudeResponse { Content = [Replacement("first", "Original", "Edited"), Replacement("second", "document", "text")] }
            : new ClaudeResponse { Content = [ClaudeContentBlock.TextBlock("Resumed before all decisions")] });
        var (service, conversation, advice) = CreateService(services, client);
        await Run(service, services, conversation.Id);
        var proposals = advice.GetPendingForConversation(conversation.Id);
        var first = proposals.Single(item => item.ToolCallId == "first");
        var second = proposals.Single(item => item.ToolCallId == "second");

        service.ApproveAdvice(conversation.Id, first.Id);
        await ExecuteApproved(service, services, conversation.Id, first.Id);

        // Characterization, not the future contract: OrderedRunner migration should wait for all
        // approvals/results before resuming instead of sending an unresolved sibling call to the model.
        Assert.AreEqual(2, client.Requests.Count);
        Assert.AreEqual(AgentState.Completed, conversation.State);
        Assert.AreEqual(AdviceStatus.Pending, second.Status);
        CollectionAssert.AreEqual(new[] { second.Id }, conversation.PendingAdviceIds);
        Assert.AreEqual("Original document", second.DocumentContentSnapshot);
        Assert.AreEqual("Edited document", conversation.DocumentContent);
        Assert.IsFalse(conversation.Messages.Any(message => message.Role == "tool" && message.ToolCallId == "second"));
    }

    [TestMethod]
    public async Task MultipleProposals_LastRejectionCompletesWithoutResumingModel_LegacyBehavior()
    {
        using var services = CreateToolServices();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), _ => new ClaudeResponse
        {
            Content = [Replacement("first", "Original", "Edited"), Replacement("second", "document", "text")]
        });
        var (service, conversation, advice) = CreateService(services, client);
        await Run(service, services, conversation.Id);
        var proposals = advice.GetPendingForConversation(conversation.Id);

        service.RejectAdvice(conversation.Id, proposals[0].Id);
        Assert.AreEqual(AgentState.AwaitingApproval, conversation.State);
        Assert.AreEqual(1, conversation.PendingAdviceIds.Count);
        service.RejectAdvice(conversation.Id, proposals[1].Id);

        // Legacy baseline only: a future runner may resume once every result is present.
        Assert.AreEqual(AgentState.Completed, conversation.State);
        Assert.AreEqual(0, conversation.PendingAdviceIds.Count);
        Assert.IsTrue(proposals.All(item => item.Status == AdviceStatus.Rejected));
        var results = conversation.Messages.Where(message => message.Role == "tool").ToArray();
        CollectionAssert.AreEquivalent(new[] { "first", "second" }, results.Select(message => message.ToolCallId).ToArray());
        Assert.IsTrue(results.All(message => message.Content!.StartsWith("REJECTED:", StringComparison.Ordinal)));
        Assert.AreEqual("Original document", conversation.DocumentContent);
        Assert.IsFalse(conversation.DocumentContentUpdated);
        Assert.AreEqual(1, client.Requests.Count);
        Assert.AreEqual(0, services.GetRequiredService<ServiceTaskQueue>().GetPendingTasks().Count());
    }

    [TestMethod]
    [DataRow("ReadFullDocument", "1|First\n2|Second")]
    [DataRow("ReadDocumentLines", "2|Second")]
    public async Task ExecutionSeam_AdapterUsesRealScopedReadHandler(string toolName, string expected)
    {
        using var services = CreateToolServices();
        var conversation = new AgentConversation
        {
            UserId = "trusted-owner", DocumentId = Guid.NewGuid(), DocumentContent = "First\nSecond"
        };
        var tool = new ToolRegistry(services).GetTool(toolName)!;
        IServiceProvider executionServices = services;
        var adapter = AgentToolAdapter.ForReadOnly(tool, async (arguments, _) =>
            await AgentToolExecution.ExecuteMcpAsync(executionServices, tool,
                arguments.ToObject<Dictionary<string, object?>>()!, conversation));
        var args = Newtonsoft.Json.Linq.JObject.Parse("{\"startLine\":2,\"endLine\":2,\"UserId\":\"untrusted\"}");
        var original = args.DeepClone();

        var result = await adapter.ExecuteAsync(args, default);

        Assert.AreEqual(expected.Replace("\n", Environment.NewLine), result.ToObject<string>());
        Assert.IsTrue(Newtonsoft.Json.Linq.JToken.DeepEquals(original, args));
        Assert.AreEqual(string.Empty, services.GetRequiredService<CurrentUserService>().DocumentContent);
    }

    [TestMethod]
    [DataRow("same", "changed", true, "changed same", true)]
    [DataRow("SAME", "changed", true, "same same", false)]
    [DataRow("missing", "changed", true, "same same", false)]
    [DataRow("same", "", true, " same", true)]
    [DataRow("same", "changed", false, "changed same", true)]
    public async Task ExecutionSeam_ApprovedAdapterPreservesLegacyReplacement(
        string oldText, string newText, bool documentExists, string expected, bool updated)
    {
        using var services = CreateToolServices();
        // ReplaceText persistence must use the separately supplied factory, not the MCP provider.
        using var mcpServices = new ServiceCollection().BuildServiceProvider();
        var conversation = new AgentConversation
        {
            UserId = "owner", DocumentId = Guid.NewGuid(), DocumentContent = "same same"
        };
        var previousUpdate = DateTime.UtcNow.AddDays(-1);
        if (documentExists)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = conversation.DocumentId, UserId = conversation.UserId,
                Content = conversation.DocumentContent, UpdatedAt = previousUpdate
            });
            await db.SaveChangesAsync();
        }
        var tool = new ToolRegistry(services).GetTool("ReplaceText")!;
        // This fixture is the approving caller; the internal execution seam does not authorize.
        IServiceProvider executionServices = mcpServices;
        var persistenceScopes = services.GetRequiredService<IServiceScopeFactory>();
        var adapter = AgentToolAdapter.ForApprovedWrite(tool, async (arguments, _) =>
            await AgentToolExecution.ExecuteApprovedAsync(executionServices,
                persistenceScopes, tool,
                arguments.ToObject<Dictionary<string, object?>>()!, conversation));
        var args = new Newtonsoft.Json.Linq.JObject { ["oldString"] = oldText, ["newString"] = newText };
        var original = args.DeepClone();

        var result = await adapter.ExecuteAsync(args, default);

        Assert.AreEqual(expected, conversation.DocumentContent);
        Assert.AreEqual(updated, conversation.DocumentContentUpdated);
        Assert.AreEqual(updated
            ? "Document updated successfully. 1 lines replaced with 1 lines."
            : "Error: old_string not found in current document. The document may have been modified since the edit was proposed.",
            result.ToObject<string>());
        Assert.IsTrue(Newtonsoft.Json.Linq.JToken.DeepEquals(original, args));
        using var verification = services.CreateScope();
        var stored = await verification.ServiceProvider.GetRequiredService<TemplateDbContext>()
            .MarkdownDocuments.AsNoTracking().SingleOrDefaultAsync();
        if (documentExists)
        {
            Assert.IsNotNull(stored);
            Assert.AreEqual(expected, stored.Content);
            if (updated) Assert.IsTrue(stored.UpdatedAt > previousUpdate);
            else Assert.AreEqual(previousUpdate, stored.UpdatedAt);
        }
        else Assert.IsNull(stored);
    }

    [TestMethod]
    public async Task ExecutionSeam_PersistenceFailurePropagatesAfterLegacyMemoryMutation()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var conversation = new AgentConversation { DocumentContent = "old" };
        var tool = new ToolRegistry(services).GetTool("ReplaceText")!;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => AgentToolExecution.ExecuteApprovedAsync(
            services, services.GetRequiredService<IServiceScopeFactory>(), tool,
            new() { ["oldString"] = "old", ["newString"] = "new" }, conversation));

        Assert.AreEqual("new", conversation.DocumentContent);
        Assert.IsFalse(conversation.DocumentContentUpdated);
    }

    private static ClaudeContentBlock Replacement(string id, string oldText, string newText) =>
        ClaudeContentBlock.ToolUse(id, "ReplaceText", new() { ["oldString"] = oldText, ["newString"] = newText });

    private static ServiceProvider CreateToolServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTaskQueueEngine();
        services.AddScoped<CurrentUserService>();
        services.AddScoped<DocumentReadTools>();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<InMemoryContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<TemplateDbContext>(provider => provider.GetRequiredService<InMemoryContext>());
        return services.BuildServiceProvider();
    }

    // Keep the real queue stopped: public APIs enqueue normally, then tests invoke the existing
    // execution seam deterministically without hosted workers, sleeps or a full web application.
    private static Task ExecuteApproved(AgentService service, IServiceProvider services, Guid id, Guid adviceId) =>
        (Task)typeof(AgentService).GetMethod("ExecuteAdviceAndResume", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, [services, id, adviceId])!;

    private static (AgentService Service, AgentConversation Conversation, AdviceService Advice) CreateService(
        IServiceProvider services, ClaudeClient client)
    {
        var advice = new AdviceService();
        var service = new AgentService(services.GetService<ServiceTaskQueue>()!, new ToolRegistry(services), advice, client,
            services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentService>.Instance);
        var conversation = new AgentConversation
        {
            UserId = "owner",
            DocumentId = Guid.NewGuid(),
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
