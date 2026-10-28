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
        Assert.AreEqual("Tool unavailable.", result.Content!.ToString());
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
    public async Task ModelException_ProjectsSafeError()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(),
            _ => throw new InvalidOperationException("fixture failure"));
        var (service, conversation, _) = CreateService(services, client);

        await Run(service, services, conversation.Id);

        Assert.AreEqual(AgentState.Error, conversation.State);
        Assert.AreEqual("Model execution failed.", conversation.ErrorMessage);
        Assert.AreEqual("Original document", conversation.DocumentContent);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    public async Task WriteProposal_MissingOrBlankCallIdRejectsBeforeDispatch(string? callId)
    {
        using var services = CreateToolServices();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), _ => new ClaudeResponse
        {
            Content = [new ClaudeContentBlock
            {
                Type = "tool_use", Id = callId, Name = "ReplaceText",
                Input = new() { ["oldString"] = "Original", ["newString"] = "Edited" }
            }]
        });
        var (service, conversation, advice) = CreateService(services, client);

        await Run(service, services, conversation.Id);

        Assert.AreEqual(AgentState.Error, conversation.State);
        Assert.AreEqual(0, advice.GetPendingForConversation(conversation.Id).Count);
        Assert.AreEqual(0, conversation.PendingAdviceIds.Count);
        Assert.AreEqual("Original document", conversation.DocumentContent);
        Assert.AreEqual(1, client.Requests.Count);
    }

    [TestMethod]
    public async Task CancelRun_DuringModelCallDoesNotStopCapturedConversationOrNewAdvice_LegacyBehavior()
    {
        using var services = CreateToolServices();
        Action cancel = () => Assert.Fail("Cancellation callback was not initialized.");
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), _ =>
        {
            // ExecuteReActLoop already captured the conversation before entering this model call.
            cancel();
            return new ClaudeResponse { Content = [Replacement("late-write", "Original", "Edited")] };
        });
        var (service, conversation, advice) = CreateService(services, client);

        cancel = () => service.CancelRun(conversation.Id);

        await Run(service, services, conversation.Id);

        Assert.IsNull(service.GetConversation(conversation.Id));
        var late = advice.GetPendingForConversation(conversation.Id).Single();
        Assert.AreEqual("late-write", late.ToolCallId);
        CollectionAssert.AreEqual(new[] { late.Id }, conversation.PendingAdviceIds);
        Assert.AreEqual(AgentState.AwaitingApproval, conversation.State);
        Assert.AreEqual("Original document", conversation.DocumentContent);
        Assert.AreEqual(1, client.Requests.Count);
        Assert.AreEqual(0, services.GetRequiredService<ServiceTaskQueue>().GetPendingTasks().Count());
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
                Id = conversation.DocumentId,
                UserId = conversation.UserId,
                Content = conversation.DocumentContent,
                UpdatedAt = previousUpdate
            });
            await db.SaveChangesAsync();
        }
        await Run(service, services, conversation.Id);
        var proposal = advice.GetPendingForConversation(conversation.Id).Single();

        service.ApproveAdvice(conversation.Id, proposal.Id);
        Assert.AreEqual(AdviceStatus.Approved, proposal.Status);
        Assert.AreEqual(0, conversation.PendingAdviceIds.Count);
        service.ApproveAdvice(conversation.Id, proposal.Id); // Duplicate must not queue a second dispatch.
        Assert.IsNull(service.ContinueRun(conversation.Id, "owner", "race"));
        proposal.Parameters["newString"] = "untrusted mutation";
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
    public async Task InvalidInitialImport_ProjectsSafeErrorInsteadOfRemainingThinking()
    {
        using var services = CreateToolServices();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), _ => new ClaudeResponse());
        var (service, conversation, advice) = CreateService(services, client);
        advice.Create(conversation.Id, "ReplaceText", "", "", new(), "", "unimportable");
        await Run(service, services, conversation.Id);
        Assert.AreEqual(AgentState.Error, conversation.State);
        Assert.AreEqual("Session execution failed.", conversation.ErrorMessage);
        Assert.AreEqual(0, client.Requests.Count);
    }

    [TestMethod]
    public async Task SiblingApproval_DuringSlowWriteIsAcceptedImmediatelyAndExecutesOnce()
    {
        var pause = new PausedSave();
        using var services = CreateToolServices(pause);
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), index => index == 0
            ? new ClaudeResponse { Content = [Replacement("first", "Original", "Edited"), Replacement("second", "document", "text")] }
            : new ClaudeResponse { Content = [ClaudeContentBlock.TextBlock("Done")] });
        var (service, conversation, advice) = CreateService(services, client);
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = conversation.DocumentId,
                UserId = conversation.UserId,
                Content = conversation.DocumentContent
            });
            await db.SaveChangesAsync();
        }
        await Run(service, services, conversation.Id);
        var first = advice.GetPendingForConversation(conversation.Id).Single(item => item.ToolCallId == "first");
        var second = advice.GetPendingForConversation(conversation.Id).Single(item => item.ToolCallId == "second");
        service.ApproveAdvice(conversation.Id, first.Id);
        pause.Armed = true;
        var dispatch = DrainDecisions(service, services);
        await pause.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            service.ApproveAdvice(conversation.Id, second.Id);
            service.ApproveAdvice(conversation.Id, second.Id);
            Assert.AreEqual(AdviceStatus.Approved, second.Status);
            Assert.AreEqual(0, conversation.PendingAdviceIds.Count);
            Assert.AreEqual(2, services.GetRequiredService<ServiceTaskQueue>().GetPendingTasks().Count());
            Assert.IsNull(service.ContinueRun(conversation.Id, "owner", "race"));
        }
        finally { pause.Release.TrySetResult(); }
        await dispatch;
        await DrainDecisions(service, services);
        Assert.AreEqual(AdviceStatus.Executed, first.Status);
        Assert.AreEqual(AdviceStatus.Executed, second.Status);
        Assert.AreEqual("Edited text", conversation.DocumentContent);
        Assert.AreEqual(2, client.Requests.Count);
        Assert.AreEqual(2, pause.Saves);
    }

    private sealed class PausedSave : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public int Saves { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Saves++;
                Entered.TrySetResult();
                await Release.Task;
            }
            return result;
        }
    }

    [TestMethod]
    public async Task QueuedApproval_RemovedConversationCannotExecuteOrApproveAgain()
    {
        using var services = CreateToolServices();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), _ =>
            new ClaudeResponse { Content = [Replacement("write", "Original", "Edited")] });
        var (service, conversation, advice) = CreateService(services, client);
        await Run(service, services, conversation.Id);
        var proposal = advice.GetPendingForConversation(conversation.Id).Single();
        service.ApproveAdvice(conversation.Id, proposal.Id);
        service.CancelRun(conversation.Id);
        service.ApproveAdvice(conversation.Id, proposal.Id);
        await DrainDecisions(service, services);
        Assert.IsNull(service.GetConversation(conversation.Id));
        Assert.IsNull(advice.Get(proposal.Id));
        Assert.AreEqual("Original document", conversation.DocumentContent);
        Assert.AreEqual(1, client.Requests.Count);
        Assert.AreEqual(1, services.GetRequiredService<ServiceTaskQueue>().GetPendingTasks().Count());
    }

    [TestMethod]
    public async Task QueuedApproval_DuplicateCallbackDoesNotRepeatWriteOrResume()
    {
        using var services = CreateToolServices();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), index => index == 0
            ? new ClaudeResponse { Content = [Replacement("write", "Original", "Original edited")] }
            : new ClaudeResponse { Content = [ClaudeContentBlock.TextBlock("Done")] });
        var (service, conversation, advice) = CreateService(services, client);
        await Run(service, services, conversation.Id);
        var proposal = advice.GetPendingForConversation(conversation.Id).Single();
        service.ApproveAdvice(conversation.Id, proposal.Id);
        service.ApproveAdvice(conversation.Id, proposal.Id);
        Assert.AreEqual(AdviceStatus.Approved, proposal.Status);
        Assert.AreEqual(AgentState.Thinking, conversation.State);
        Assert.AreEqual(0, conversation.PendingAdviceIds.Count);
        Assert.IsNull(service.ContinueRun(conversation.Id, "owner", "must wait"));
        await DrainDecisions(service, services);
        await DrainDecisions(service, services);
        Assert.AreEqual("Original edited document", conversation.DocumentContent);
        Assert.AreEqual(2, client.Requests.Count);
        Assert.AreEqual(AgentState.Completed, conversation.State);
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
        // Canonical continuation is appended by the queued session, never imported from UI.
        Assert.AreEqual(history.Length, conversation.Messages.Count);
        Assert.AreEqual($"AgentContinue-{conversation.Id}", services.GetRequiredService<ServiceTaskQueue>().GetPendingTasks().Single().TaskName);
        await Run(service, services, conversation.Id);

        Assert.AreEqual(20, client.Requests.Count);
        Assert.AreEqual(10, conversation.LoopCount);
        Assert.AreEqual(AgentState.Completed, conversation.State);
        Assert.AreEqual("Question", client.Requests[0].Messages.Single().Content);
        Assert.AreEqual("Continue", client.Requests[10].Messages[^1].Content);
    }

    [TestMethod]
    public async Task MultipleProposals_FirstApprovalWaitsForOtherPendingAdvice()
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

        Assert.AreEqual(1, client.Requests.Count);
        Assert.AreEqual(AgentState.AwaitingApproval, conversation.State);
        Assert.AreEqual(AdviceStatus.Pending, second.Status);
        CollectionAssert.AreEqual(new[] { second.Id }, conversation.PendingAdviceIds);
        Assert.AreEqual("Original document", second.DocumentContentSnapshot);
        Assert.AreEqual("Edited document", conversation.DocumentContent);
        Assert.IsFalse(conversation.Messages.Any(message => message.Role == "tool" && message.ToolCallId == "second"));
    }

    [TestMethod]
    public async Task MultipleProposals_LastRejectionQueuesBatchCompletionResume()
    {
        using var services = CreateToolServices();
        var client = new ScriptedClient(services.GetRequiredService<IServiceScopeFactory>(), index => index == 0
            ? new ClaudeResponse { Content = [Replacement("first", "Original", "Edited"), Replacement("second", "document", "text")] }
            : new ClaudeResponse { Content = [ClaudeContentBlock.TextBlock("Rejected changes acknowledged")] });
        var (service, conversation, advice) = CreateService(services, client);
        await Run(service, services, conversation.Id);
        var proposals = advice.GetPendingForConversation(conversation.Id);

        service.RejectAdvice(conversation.Id, proposals[0].Id);
        Assert.AreEqual(AgentState.Thinking, conversation.State);
        Assert.AreEqual(1, conversation.PendingAdviceIds.Count);
        service.RejectAdvice(conversation.Id, proposals[1].Id);

        Assert.AreEqual(AgentState.Thinking, conversation.State);
        Assert.IsNull(service.ContinueRun(conversation.Id, "owner", "racing continuation"));
        await DrainDecisions(service, services);
        Assert.AreEqual(0, conversation.PendingAdviceIds.Count);
        Assert.IsTrue(proposals.All(item => item.Status == AdviceStatus.Rejected));
        var results = conversation.Messages.Where(message => message.Role == "tool").ToArray();
        CollectionAssert.AreEquivalent(new[] { "first", "second" }, results.Select(message => message.ToolCallId).ToArray());
        Assert.IsTrue(results.All(message => message.Content!.StartsWith("REJECTED:", StringComparison.Ordinal)));
        Assert.AreEqual("Original document", conversation.DocumentContent);
        Assert.IsFalse(conversation.DocumentContentUpdated);
        Assert.AreEqual(2, client.Requests.Count);
    }

    [TestMethod]
    [DataRow("ReadFullDocument", "1|First\n2|Second")]
    [DataRow("ReadDocumentLines", "2|Second")]
    public async Task ExecutionSeam_AdapterUsesRealScopedReadHandler(string toolName, string expected)
    {
        using var services = CreateToolServices();
        var conversation = new AgentConversation
        {
            UserId = "trusted-owner",
            DocumentId = Guid.NewGuid(),
            DocumentContent = "First\nSecond"
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
            UserId = "owner",
            DocumentId = Guid.NewGuid(),
            DocumentContent = "same same"
        };
        var previousUpdate = DateTime.UtcNow.AddDays(-1);
        if (documentExists)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = conversation.DocumentId,
                UserId = conversation.UserId,
                Content = conversation.DocumentContent,
                UpdatedAt = previousUpdate
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

    private static ServiceProvider CreateToolServices(Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor? interceptor = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTaskQueueEngine();
        services.AddScoped<CurrentUserService>();
        services.AddScoped<DocumentReadTools>();
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<InMemoryContext>(options =>
        {
            options.UseInMemoryDatabase(databaseName);
            if (interceptor != null) options.AddInterceptors(interceptor);
        });
        services.AddScoped<TemplateDbContext>(provider => provider.GetRequiredService<InMemoryContext>());
        return services.BuildServiceProvider();
    }

    // Keep the real queue stopped: public APIs enqueue normally, then tests invoke the existing
    // execution seam deterministically without hosted workers, sleeps or a full web application.
    private static Task ExecuteApproved(AgentService service, IServiceProvider services, Guid id, Guid adviceId) =>
        DrainDecisions(service, services);

    private static async Task DrainDecisions(AgentService service, IServiceProvider services)
    {
        var queue = services.GetRequiredService<ServiceTaskQueue>();
        foreach (var task in queue.GetPendingTasks().ToArray())
            await task.TaskAction(services);
    }

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
        services.GetService<ServiceTaskQueue>()?.GetPendingTasks().Any() == true
            ? DrainDecisions(service, services)
            : (Task)typeof(AgentService).GetMethod("ExecuteSession", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(service, [id])!;

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
