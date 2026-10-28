using Aiursoft.AgentKit;
using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Messages;
using Aiursoft.AgentKit.Evaluator;
using Aiursoft.MarkToHtml.AgentExam;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.InMemory;
using Aiursoft.MarkToHtml.Services.Agent;
using Aiursoft.MarkToHtml.Services.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class AgentSessionFacadeTests
{
    [TestMethod]
    public async Task StartPartialApprovalAndRejection_ComposeRealBindingsAndReplayReasoning()
    {
        using var f = new Fixture(Calls(Read("r"), Write("w1", "Original", "Edited"), Write("w2", "document", "text")), Done());
        var paused = await f.Session.StartAsync();
        var ids = f.Conversation.PendingAdviceIds.ToArray();
        Assert.AreEqual(2, ids.Length);
        Assert.AreEqual(AgentState.AwaitingApproval, f.Conversation.State);
        Assert.AreEqual(1, f.Conversation.LoopCount);
        Assert.AreEqual("batch thought", f.Conversation.Messages.Single(m => m.Role == "assistant").ReasoningContent);
        Assert.AreEqual("1|Original document", f.Conversation.Messages.Single(m => m.Role == "tool").Content);
        Assert.AreEqual("Original document", f.Advice.Get(ids[0])!.DocumentContentSnapshot);
        Assert.AreEqual("Original document", f.Stored());
        // Neither snapshots nor UI proposal parameters are dispatch inputs.
        paused.Checkpoint!.Resolutions[1].Call.Arguments["newString"] = "poison";
        f.Advice.Get(ids[0])!.Parameters["newString"] = "poison";
        var adviceService = f.Advice;
        f.OnDbResolve = () => Assert.AreEqual(AdviceStatus.Approved, adviceService.Get(ids[0])!.Status,
            "Approval must be visible before persistence dispatch.");
        var partial = await f.Session.ResolveAsync(ids[0], ApprovalDecision.Execute);
        f.OnDbResolve = null;
        Assert.AreEqual(OrderedAgentRunOutcome.AwaitingApproval, partial.Outcome);
        Assert.AreEqual(1, f.Client.Requests.Count);
        Assert.AreEqual("Edited document", f.Stored());
        Assert.AreEqual(AdviceStatus.Executed, f.Advice.Get(ids[0])!.Status);
        CollectionAssert.AreEqual(new[] { ids[1] }, f.Conversation.PendingAdviceIds.ToArray());
        var completed = await f.Session.ResolveAsync(ids[1], ApprovalDecision.Reject);
        Assert.AreEqual(OrderedAgentRunOutcome.Completed, completed.Outcome);
        Assert.AreEqual(AgentState.Completed, f.Conversation.State);
        Assert.AreEqual(2, f.Conversation.LoopCount);
        Assert.AreEqual(AdviceStatus.Rejected, f.Advice.Get(ids[1])!.Status);
        Assert.AreEqual(AgentToolResultMapper.Rejected, f.Advice.Get(ids[1])!.Result);
        Assert.AreEqual("Edited document", f.Stored());
        Assert.AreEqual("final thought", f.Conversation.Messages.Last().ReasoningContent);
        var replay = f.Client.Requests[1];
        Assert.AreEqual("batch thought", replay.Single(m => m.Role == "assistant").ReasoningContent);
        Assert.IsTrue(replay.SelectMany(m => m.Content as List<ClaudeContentBlock> ?? []).Any(b =>
            b.ToolUseId == "w2" && Equals(b.Content, AgentToolResultMapper.Rejected)));
        Assert.AreEqual(ToolOutcome.Rejected, completed.Results.Single(r => r.CallId == "w2").Outcome);
        Assert.AreEqual(0, f.Conversation.PendingAdviceIds.Count);
    }

    [TestMethod]
    public async Task ForeignAndStaleAdvice_CannotResolveReusedCallIdEvenIfStatusIsPending()
    {
        using var f = new Fixture(Calls(Write("same", "Original", "Edited")),
            Calls(Write("same", "document", "text")), Done());
        await f.Session.StartAsync();
        var old = f.Conversation.PendingAdviceIds.Single();
        var foreign = f.Advice.Create(Guid.NewGuid(), "ReplaceText", "", "", new(), "", "same");
        var forged = f.Advice.Create(f.Conversation.Id, "ReplaceText", "", "", new(), "", "same");
        var before = JToken.FromObject((await f.Session.GetSnapshotAsync())!);
        foreach (var id in new[] { foreign.Id, forged.Id })
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Session.ResolveAsync(id, ApprovalDecision.Execute));
        Assert.IsTrue(JToken.DeepEquals(before, JToken.FromObject((await f.Session.GetSnapshotAsync())!)));
        Assert.AreEqual("Original document", f.Stored());
        await f.Session.ResolveAsync(old, ApprovalDecision.Execute);
        var current = f.Conversation.PendingAdviceIds.Single();
        Assert.AreNotEqual(old, current);
        f.Advice.UpdateStatus(old, AdviceStatus.Pending); // Adversarial stale mutable service state.
        before = JToken.FromObject((await f.Session.GetSnapshotAsync())!);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Session.ResolveAsync(old, ApprovalDecision.Execute));
        Assert.IsTrue(JToken.DeepEquals(before, JToken.FromObject((await f.Session.GetSnapshotAsync())!)));
        Assert.AreEqual(AdviceStatus.Pending, f.Advice.Get(current)!.Status);
        Assert.AreEqual("Edited document", f.Stored());
        Assert.AreEqual(2, f.Client.Requests.Count);
        await f.Session.ResolveAsync(current, ApprovalDecision.Reject);
        Assert.AreEqual("Edited document", f.Stored());
    }

    [TestMethod]
    public async Task ConcurrentDuplicateApproval_ExecutesOnceAndKeepsCanonicalSnapshotsIndependent()
    {
        using var f = new Fixture(Calls(Write("w", "Original", "Edited")), Done());
        await f.Session.StartAsync();
        var id = f.Conversation.PendingAdviceIds.Single();
        var requests = Enumerable.Range(0, 2).Select(async _ =>
        {
            try { await f.Session.ResolveAsync(id, ApprovalDecision.Execute); return true; }
            catch (InvalidOperationException) { return false; }
        }).ToArray();
        Assert.AreEqual(1, (await Task.WhenAll(requests)).Count(success => success));
        Assert.AreEqual(1, f.Dispatches);
        Assert.AreEqual(2, f.Client.Requests.Count);
        Assert.AreEqual("Edited document", f.Stored());
        var snapshot = (await f.Session.GetSnapshotAsync())!;
        ((JValue)snapshot.Results.Single().Output!).Value = "tampered";
        f.Conversation.Messages.Clear();
        Assert.AreNotEqual("tampered", (await f.Session.GetSnapshotAsync())!.Results.Single().Output!.Value<string>());
        Assert.AreEqual(OrderedAgentRunOutcome.Completed, (await f.Session.GetSnapshotAsync())!.Outcome);
    }

    [TestMethod]
    public async Task TerminalOutcomes_RetainCanonicalBudgetAndRedactModelFailure()
    {
        using var budget = new Fixture(Enumerable.Range(0, 10).Select(i => Calls(Read($"r{i}"))).ToArray());
        var result = await budget.Session.StartAsync();
        Assert.AreEqual(OrderedAgentRunOutcome.MaxIterations, result.Outcome);
        Assert.AreEqual(10, budget.Conversation.LoopCount);
        Assert.AreEqual(AgentState.Completed, budget.Conversation.State);
        StringAssert.StartsWith(budget.Conversation.Messages.Last().Content!, "I've reached the maximum number of steps.");
        Assert.AreEqual(OrderedAgentRunOutcome.MaxIterations, (await budget.Session.GetSnapshotAsync())!.Outcome);
        using var failure = new Fixture();
        failure.Client.Error = new InvalidOperationException("sensitive provider body");
        result = await failure.Session.StartAsync();
        Assert.AreEqual(OrderedAgentRunOutcome.ModelFailure, result.Outcome);
        Assert.AreEqual(AgentState.Error, failure.Conversation.State);
        Assert.AreEqual("Model execution failed.", failure.Conversation.ErrorMessage);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    public async Task WriteProposal_MissingOrBlankCallIdBecomesModelFailure_OptInOnly(string? callId)
    {
        using var f = new Fixture(Calls(new ClaudeContentBlock
        {
            Type = "tool_use", Id = callId, Name = "ReplaceText",
            Input = new() { ["oldString"] = "Original", ["newString"] = "Edited" }
        }));

        var result = await f.Session.StartAsync();

        // Unlike the LegacyBehavior baseline, validation rejects the entire completion before
        // canonical append or Advice creation. This difference still blocks production wiring.
        Assert.AreEqual(OrderedAgentRunOutcome.ModelFailure, result.Outcome);
        Assert.AreEqual(AgentState.Error, f.Conversation.State);
        Assert.AreEqual("Model execution failed.", f.Conversation.ErrorMessage);
        Assert.IsNull(result.Checkpoint);
        Assert.AreEqual(0, result.Transcript.Count(message => message.Role == TranscriptRole.Assistant));
        Assert.AreEqual(0, f.Conversation.PendingAdviceIds.Count);
        Assert.AreEqual(0, f.Advice.GetPendingForConversation(f.Conversation.Id).Count);
        Assert.AreEqual("Original document", f.Stored());
        Assert.AreEqual(0, f.Dispatches);
        Assert.AreEqual(1, f.Client.Requests.Count);
    }

    [TestMethod]
    public async Task WriteException_FailsAdviceAndWaitsForSibling_WithoutRetryingWrite()
    {
        using var f = new Fixture(Calls(Write("w1", "Original", "Edited"), Write("w2", "document", "text")), Done());
        await f.Session.StartAsync();
        var ids = f.Conversation.PendingAdviceIds.ToArray();
        f.OnDbResolve = () => throw new InvalidOperationException("private persistence detail");
        var partial = await f.Session.ResolveAsync(ids[0], ApprovalDecision.Execute);
        Assert.AreEqual(OrderedAgentRunOutcome.AwaitingApproval, partial.Outcome);
        var failure = partial.Checkpoint!.Resolutions.Single(item => item.Call.Id == "w1").Result!;
        Assert.AreEqual(ToolOutcome.Failed, failure.Outcome);
        Assert.IsNull(failure.Output);
        Assert.AreEqual("Tool execution failed.", failure.Error);
        Assert.AreEqual(AdviceStatus.Failed, f.Advice.Get(ids[0])!.Status);
        StringAssert.StartsWith(f.Advice.Get(ids[0])!.Error!, "DOCUMENT_PERSISTENCE_FAILED:");
        StringAssert.Contains(f.Advice.Get(ids[0])!.Error!, "saved document state is unknown");
        StringAssert.Contains(f.Conversation.Messages.Last().Content!, "Do not blindly repeat");
        Assert.AreEqual(AgentState.AwaitingApproval, f.Conversation.State);
        CollectionAssert.AreEqual(new[] { ids[1] }, f.Conversation.PendingAdviceIds.ToArray());
        Assert.AreEqual(1, f.Client.Requests.Count);
        // Legacy execution updates memory before persistence; failure does not roll it back.
        Assert.AreEqual("Edited document", f.Conversation.DocumentContent);
        Assert.AreEqual("Original document", f.Stored());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Session.ResolveAsync(ids[0], ApprovalDecision.Execute));
        f.OnDbResolve = null;
        var completed = await f.Session.ResolveAsync(ids[1], ApprovalDecision.Reject);
        Assert.AreEqual(OrderedAgentRunOutcome.Completed, completed.Outcome);
        Assert.AreEqual(ToolOutcome.Failed, completed.Results.Single(item => item.CallId == "w1").Outcome);
        Assert.AreEqual(AdviceStatus.Failed, f.Advice.Get(ids[0])!.Status);
        Assert.AreEqual(2, f.Client.Requests.Count);
        Assert.IsFalse(JToken.FromObject(completed).ToString().Contains("private persistence detail"));
    }

    [TestMethod]
    public async Task FailedWrite_FeedbackReachesModel_CorrectedWriteStillRequiresApproval()
    {
        using var f = new Fixture(Calls(Write("same", "Original", "Edited")),
            Calls(Read("verify")), Calls(Write("same", "Edited", "Corrected")), Done(), Done());
        await f.Session.StartAsync();
        var failedId = f.Conversation.PendingAdviceIds.Single();
        f.OnDbResolve = () => throw new InvalidOperationException("secret connection string");
        var retry = await f.Session.ResolveAsync(failedId, ApprovalDecision.Execute);
        Assert.AreEqual(OrderedAgentRunOutcome.AwaitingApproval, retry.Outcome);
        Assert.AreEqual(3, f.Client.Requests.Count);
        var feedback = f.Client.Requests[1].SelectMany(message => message.Content as List<ClaudeContentBlock> ?? [])
            .Single(block => block.ToolUseId == "same").Content!.ToString()!;
        StringAssert.Contains(feedback, "DOCUMENT_PERSISTENCE_FAILED");
        StringAssert.Contains(feedback, "not proof of persistence");
        Assert.IsFalse(feedback.Contains("secret connection string"));
        Assert.AreEqual("Edited document", f.Conversation.DocumentContent);
        Assert.AreEqual("Original document", f.Stored());
        var correctedId = f.Conversation.PendingAdviceIds.Single();
        Assert.AreNotEqual(failedId, correctedId);
        Assert.AreEqual(AdviceStatus.Pending, f.Advice.Get(correctedId)!.Status);
        f.OnDbResolve = null;
        var completed = await f.Session.ResolveAsync(correctedId, ApprovalDecision.Execute);
        Assert.AreEqual(OrderedAgentRunOutcome.Completed, completed.Outcome);
        Assert.AreEqual("Corrected document", f.Stored());
        Assert.AreEqual(AdviceStatus.Failed, f.Advice.Get(failedId)!.Status);
        Assert.AreEqual(AdviceStatus.Executed, f.Advice.Get(correctedId)!.Status);
        var replay = f.Client.Requests.Last().SelectMany(message => message.Content as List<ClaudeContentBlock> ?? [])
            .Where(block => block.ToolUseId == "same").ToArray();
        Assert.AreEqual(2, replay.Length);
        StringAssert.StartsWith(replay[0].Content!.ToString()!, "DOCUMENT_PERSISTENCE_FAILED:");
        StringAssert.StartsWith(replay[1].Content!.ToString()!, "Document updated successfully.");
        Assert.AreEqual("Tool execution failed.", completed.Transcript.SelectMany(message => message.ToolResults)
            .Single(result => result.Outcome == ToolOutcome.Failed).Error);
        await f.Session.ContinueAsync("trusted", "review saved changes");
        var continuedFeedback = f.Client.Requests.Last().SelectMany(message => message.Content as List<ClaudeContentBlock> ?? [])
            .First(block => block.ToolUseId == "same").Content!.ToString()!;
        StringAssert.StartsWith(continuedFeedback, "DOCUMENT_PERSISTENCE_FAILED:");
        Assert.AreEqual(AdviceStatus.Failed, f.Advice.Get(failedId)!.Status);
    }

    [TestMethod]
    public async Task BusinessErrorText_RemainsExecuted_NotAnExceptionFailure()
    {
        using var f = new Fixture(Calls(Write("w", "missing text", "Edited")), Done());
        await f.Session.StartAsync();
        var id = f.Conversation.PendingAdviceIds.Single();
        var completed = await f.Session.ResolveAsync(id, ApprovalDecision.Execute);
        var result = completed.Results.Single();
        Assert.AreEqual(ToolOutcome.Succeeded, result.Outcome);
        StringAssert.StartsWith(result.Output!.Value<string>()!, "Error:");
        Assert.AreEqual(AdviceStatus.Executed, f.Advice.Get(id)!.Status);
        Assert.IsNull(f.Advice.Get(id)!.Error);
        Assert.AreEqual("Original document", f.Stored());
        Assert.AreEqual(2, f.Client.Requests.Count);
    }

    [TestMethod]
    public async Task Continue_UsesCanonicalHistory_CurrentDocumentAndFreshBudget()
    {
        using var f = new Fixture(Done(), Calls(Read("r")), Done());
        var initial = await f.Session.StartAsync();
        f.Conversation.Messages.Clear(); // UI is never a replay source.
        var continued = await f.Session.ContinueAsync("trusted", "review again", "Editor\ncontents");
        Assert.AreEqual(OrderedAgentRunOutcome.Completed, continued.Outcome);
        Assert.AreEqual(2, continued.Iterations);
        Assert.AreEqual(2, f.Conversation.LoopCount);
        Assert.AreEqual("Editor\ncontents", f.Conversation.DocumentContent);
        Assert.AreEqual("Original document", f.Stored());
        var replay = f.Client.Requests[1];
        Assert.AreEqual("final thought", replay.Single(message => message.Role == "assistant").ReasoningContent);
        var tail = continued.Transcript.Skip(initial.Transcript.Count).Take(3).ToArray();
        Assert.AreEqual(AgentService.DocumentSystemReminder, ((TextBlock)tail[0].Content.Single()).Text);
        Assert.IsTrue(tail[0].IsMeta);
        Assert.IsTrue(tail[1].IsMeta);
        StringAssert.Contains(((TextBlock)tail[1].Content.Single()).Text, "1|Editor\n2|contents");
        Assert.AreEqual("review again", ((TextBlock)tail[2].Content.Single()).Text);
        Assert.IsFalse(tail[2].IsMeta);
        Assert.AreEqual("1|Editor\n2|contents", continued.Results.Single().Output!.Value<string>());
    }

    [TestMethod]
    public async Task Continue_AfterBudgetOrModelFailure_StartsNewTenCallBudget()
    {
        using var budget = new Fixture([.. Enumerable.Range(0, 10).Select(i => Calls(Read($"r{i}"))),
            .. Enumerable.Range(0, 9).Select(i => Calls(Read($"next{i}"))), Done()]);
        await budget.Session.StartAsync();
        var completed = await budget.Session.ContinueAsync("trusted", "continue");
        Assert.AreEqual(OrderedAgentRunOutcome.Completed, completed.Outcome);
        Assert.AreEqual(10, completed.Iterations);
        Assert.AreEqual(20, budget.Client.Requests.Count);
        Assert.IsTrue(budget.Client.Requests[10].SelectMany(message => message.Content as List<ClaudeContentBlock> ?? [])
            .Any(block => block.Text?.StartsWith("I've reached the maximum number of steps.") == true));
        using var failure = new Fixture(Done(), Done());
        failure.Client.Error = new InvalidOperationException("provider failure");
        await failure.Session.StartAsync();
        failure.Client.Error = null;
        completed = await failure.Session.ContinueAsync("trusted", "try again");
        Assert.AreEqual(OrderedAgentRunOutcome.Completed, completed.Outcome);
        Assert.AreEqual(1, completed.Iterations);
        Assert.IsNull(failure.Conversation.ErrorMessage);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    public async Task Continue_InvalidRequests_DoNotMutateDocumentHistoryOrAdvice(string? editorSnapshot)
    {
        using var f = new Fixture(Done(), Calls(Write("w", "Original", "Edited")));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Session.ContinueAsync("trusted", "early", "poison"));
        await f.Session.StartAsync();
        var before = JToken.FromObject((await f.Session.GetSnapshotAsync())!);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Session.ContinueAsync("foreign", "edit", "poison"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Session.ContinueAsync("trusted", " ", "poison"));
        Assert.IsTrue(JToken.DeepEquals(before, JToken.FromObject((await f.Session.GetSnapshotAsync())!)));
        await f.Session.ContinueAsync("trusted", "edit", editorSnapshot);
        var id = f.Conversation.PendingAdviceIds.Single();
        before = JToken.FromObject((await f.Session.GetSnapshotAsync())!);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Session.ContinueAsync("trusted", "busy", "poison"));
        Assert.IsTrue(JToken.DeepEquals(before, JToken.FromObject((await f.Session.GetSnapshotAsync())!)));
        Assert.AreEqual("Original document", f.Conversation.DocumentContent);
        Assert.AreEqual(id, f.Conversation.PendingAdviceIds.Single());
        Assert.AreEqual(AdviceStatus.Pending, f.Advice.Get(id)!.Status);
        Assert.AreEqual(2, f.Client.Requests.Count);
    }

    [TestMethod]
    public async Task RuntimeSnapshot_ExportsIndependentRunAndTools_WithoutPoisoningLaterEvidence()
    {
        using var f = new Fixture(Calls(Read("r"), Write("w", "Original", "Edited")), Done());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => f.Session.GetRuntimeSnapshotAsync());
        await f.Session.StartAsync();
        var snapshot = await f.Session.GetRuntimeSnapshotAsync();
        var before = JToken.FromObject(snapshot);
        snapshot.Run.Checkpoint!.Resolutions.Single(item => item.Call.Id == "w").Call.Arguments["newString"] = "poison";
        snapshot.Run.Transcript.Last().ToolCalls.Single(item => item.Id == "w").Arguments["newString"] = "poison";
        Assert.ThrowsExactly<NotSupportedException>(() => ((ICollection<string>)snapshot.AvailableTools).Clear());
        ((TranscriptMessage[])snapshot.Run.Transcript)[0] = TranscriptMessage.User("poison");
        Assert.IsTrue(JToken.DeepEquals(before, JToken.FromObject(await f.Session.GetRuntimeSnapshotAsync())));
        Assert.AreEqual("Original document", snapshot.DocumentContent);
        await f.Session.ResolveAsync(f.Conversation.PendingAdviceIds.Single(), ApprovalDecision.Execute);
        var completed = await f.Session.GetRuntimeSnapshotAsync();
        var expected = JToken.FromObject(completed);
        ((JValue)completed.Run.Results.Single(item => item.CallId == "w").Output!).Value = "poison";
        ((ToolResult[])completed.Run.Results)[0] = completed.Run.Results[0] with { Output = new JValue("poison") };
        Assert.IsTrue(JToken.DeepEquals(expected, JToken.FromObject(await f.Session.GetRuntimeSnapshotAsync())));
        var evidence = await f.GetEvidenceAsync(0);
        Assert.IsTrue(evidence.Valid);
        Assert.AreEqual("Edited", evidence.Tools.Single(tool => tool.Name == "ReplaceText").Parameters["newString"]!.Value<string>());
        Assert.AreEqual("Edited document", evidence.State["DocumentContent"]!.Value<string>());
        Assert.AreEqual("Edited document", f.Stored());
    }

    [TestMethod]
    public async Task Evidence_PartialCheckpoint_SeparatesDispatchProposalAndRejection()
    {
        using var f = new Fixture(Calls(Read("r"), Write("w1", "Original", "Edited"),
            Write("w2", "document", "text")), Done());
        await f.Session.StartAsync();
        var evidence = await f.GetEvidenceAsync(0);
        Assert.IsFalse(evidence.Valid);
        Assert.AreEqual(1, evidence.Tools.Count);
        Assert.AreEqual("ReadFullDocument", evidence.Tools.Single().Name);
        Assert.AreEqual(2, evidence.State["Resolutions"]!.Count(item => item["Outcome"]!.Value<string>() == "Unresolved"));
        var ids = f.Conversation.PendingAdviceIds.ToArray();
        await f.Session.ResolveAsync(ids[0], ApprovalDecision.Execute);
        evidence = await f.GetEvidenceAsync(0);
        Assert.AreEqual(2, evidence.Tools.Count);
        Assert.AreEqual("Edited document", evidence.State["DocumentContent"]!.Value<string>());
        Assert.AreEqual("InMemory", evidence.State["DocumentContentSource"]!.Value<string>());
        Assert.IsFalse(evidence.State["PersistenceVerified"]!.Value<bool>());
        await f.Session.ResolveAsync(ids[1], ApprovalDecision.Reject);
        evidence = await f.GetEvidenceAsync(0);
        Assert.IsTrue(evidence.Valid);
        Assert.AreEqual(2, evidence.Tools.Count);
        Assert.AreEqual("Rejected", evidence.State["Resolutions"]!.Last()["Outcome"]!.Value<string>());
        Assert.AreEqual("done", evidence.Response);
        Assert.AreEqual(2, evidence.IterationCount);
        evidence.Tools[0].Parameters["poison"] = true;
        evidence.State["DocumentContent"] = "poison";
        Assert.IsNull((await f.GetEvidenceAsync(0)).Tools[0].Parameters["poison"]);
        Assert.AreEqual("Edited document", f.Conversation.DocumentContent);
    }

    [TestMethod]
    public async Task Evidence_FailedDispatchAndBusinessError_AreNotConflated()
    {
        using var failure = new Fixture(Calls(Write("w", "Original", "Edited")), Done());
        await failure.Session.StartAsync();
        failure.OnDbResolve = () => throw new InvalidOperationException("private database details");
        await failure.Session.ResolveAsync(failure.Conversation.PendingAdviceIds.Single(), ApprovalDecision.Execute);
        var evidence = await failure.GetEvidenceAsync(0);
        Assert.AreEqual(ToolOutcome.Failed, evidence.Tools.Single().Outcome);
        Assert.AreEqual(JTokenType.Null, evidence.Tools.Single().Result.Type);
        Assert.AreEqual("Edited document", evidence.State["DocumentContent"]!.Value<string>());
        Assert.AreEqual("Original document", failure.Stored());
        Assert.IsFalse(JToken.FromObject(evidence).ToString().Contains("private database details"));
        using var business = new Fixture(Calls(Write("w", "missing", "Edited")), Done());
        await business.Session.StartAsync();
        await business.Session.ResolveAsync(business.Conversation.PendingAdviceIds.Single(), ApprovalDecision.Execute);
        evidence = await business.GetEvidenceAsync(0);
        Assert.AreEqual(ToolOutcome.Succeeded, evidence.Tools.Single().Outcome);
        StringAssert.StartsWith(evidence.Tools.Single().Result.Value<string>()!, "Error:");
    }

    [TestMethod]
    public async Task Evidence_ReusedIdsUseAbsoluteBatchIndex_ContinuationExcludesEarlierTurn()
    {
        using var f = new Fixture(Calls(Read("same")), Calls(Read("same")), Done(), Calls(Read("same")), Done());
        await f.Session.StartAsync();
        var first = await f.GetEvidenceAsync(0);
        Assert.IsTrue(first.Valid);
        Assert.AreEqual(2, first.Tools.Select(tool => tool.CallId).Distinct().Count());
        CollectionAssert.AreEqual(new[] { 1, 2 }, first.Tools.Select(tool => tool.Iteration).ToArray());
        await f.Session.ContinueAsync("trusted", "review", "Editor snapshot");
        var next = await f.GetEvidenceAsync(1);
        Assert.IsTrue(next.Valid);
        Assert.AreEqual(1, next.Tools.Count);
        Assert.IsFalse(first.Tools.Any(tool => tool.CallId == next.Tools.Single().CallId));
        Assert.AreEqual(2, next.IterationCount);
        Assert.AreEqual(1, next.Tools.Single().Iteration);
        Assert.AreEqual("Editor snapshot", next.State["DocumentContent"]!.Value<string>());
        Assert.AreEqual("Original document", f.Stored());
    }

    [TestMethod]
    public async Task Evidence_BudgetNoticeIsNotModelCompletion_AndFailedAttemptCounts()
    {
        using var budget = new Fixture([.. Enumerable.Range(0, 10).Select(_ => Calls(Read("same"))), Done()]);
        await budget.Session.StartAsync();
        var evidence = await budget.GetEvidenceAsync(0);
        Assert.IsFalse(evidence.Valid);
        Assert.AreEqual(10, evidence.Tools.Count);
        Assert.AreEqual(10, evidence.IterationCount);
        Assert.AreEqual(string.Empty, evidence.Response);
        await budget.Session.ContinueAsync("trusted", "continue");
        evidence = await budget.GetEvidenceAsync(1);
        Assert.IsTrue(evidence.Valid);
        Assert.AreEqual(0, evidence.Tools.Count);
        Assert.AreEqual(1, evidence.IterationCount);
        using var failure = new Fixture();
        failure.Client.Error = new InvalidOperationException("provider details");
        await failure.Session.StartAsync();
        evidence = await failure.GetEvidenceAsync(0);
        Assert.IsFalse(evidence.Valid);
        Assert.AreEqual(1, evidence.IterationCount);
        Assert.AreEqual(0, evidence.Tools.Count);
        Assert.AreEqual(string.Empty, evidence.Response);
    }

    [TestMethod]
    public async Task Evidence_LossyImportedSucceededPlaceholder_IsNotExecution()
    {
        using var f = new Fixture(
        [
            new() { Role = "user", Content = "old request" },
            new() { Role = "assistant", ToolCalls = [new ToolCallData
                { Id = "same", Function = new ToolCallFunction { Name = "ReadFullDocument", Arguments = "{}" } }] },
            new() { Role = "tool", ToolCallId = "same", Content = "Error: historical result" },
            new() { Role = "user", Content = "new request" }
        ], Calls(Read("same")), Done(), Done());
        var run = await f.Session.StartAsync();
        Assert.AreEqual(2, run.Transcript.SelectMany(message => message.ToolResults).Count());
        var evidence = await f.GetEvidenceAsync(0);
        Assert.IsTrue(evidence.Valid);
        Assert.AreEqual(1, evidence.Tools.Count);
        Assert.AreEqual(1, evidence.State["Resolutions"]!.Count());
        Assert.AreEqual("4:same", evidence.Tools.Single().CallId);
        await f.Session.ContinueAsync("trusted", "next");
        evidence = await f.GetEvidenceAsync(1);
        Assert.IsTrue(evidence.Valid);
        Assert.AreEqual(0, evidence.Tools.Count);
    }

    [TestMethod]
    public async Task Evidence_ReusedIdApprovalTraceLoss_PreservesObservedDispatches()
    {
        using var f = new Fixture(Calls(Read("same")), Calls(Write("same", "Original", "Edited")), Done());
        await f.Session.StartAsync();
        var before = await f.Session.GetRuntimeSnapshotAsync();
        var readTrace = before.Run.Checkpoint!.CompletedTraces.Single();
        await f.Session.ResolveAsync(f.Conversation.PendingAdviceIds.Single(), ApprovalDecision.Execute);
        var snapshot = await f.Session.GetRuntimeSnapshotAsync();
        var evidence = await f.GetEvidenceAsync(0);
        Assert.IsTrue(evidence.Valid);
        Assert.AreEqual(2, evidence.Tools.Count);
        Assert.AreEqual(2, evidence.Tools.Select(tool => tool.CallId).Distinct().Count());
        Assert.AreEqual(readTrace.Duration, evidence.Tools[0].Elapsed);
        Assert.AreEqual(snapshot.Run.Traces.Single().Duration, evidence.Tools[1].Elapsed);
        Assert.IsTrue(evidence.Tools.All(tool => tool.Outcome == ToolOutcome.Succeeded));
        Assert.AreEqual(1, snapshot.Run.Traces.Count); // Canonical runner result stays lossy.
        Assert.AreEqual("Edited document", f.Stored());
        var missing = AgentSessionEvidenceAdapter.ToStepEvidence(0, snapshot with { ObservedTraces = null });
        Assert.IsFalse(missing.Valid);
        Assert.AreEqual(0, missing.Tools.Count);
        var ledger = (Dictionary<(int Batch, string CallId), ToolTrace>)snapshot.ObservedTraces!;
        ledger.Clear();
        Assert.IsFalse(AgentSessionEvidenceAdapter.ToStepEvidence(0, snapshot).Valid);
        Assert.IsTrue((await f.GetEvidenceAsync(0)).Valid);
    }

    [TestMethod]
    public async Task Evidence_LaterReusedBatches_PartialApprovalRejectionAndNewReads_AreNotDoubleCounted()
    {
        using var f = new Fixture(Calls(Read("same")),
            Calls(Write("same", "Original", "Edited"), Write("other", "document", "text")),
            Calls(Read("fresh"), Write("same", "Edited", "Final"), Write("other", "missing", "ignored")),
            Calls(Read("after")), Done(), Calls(Write("same", "Final", "Next")), Done());
        await f.Session.StartAsync();
        var first = (await f.Session.GetRuntimeSnapshotAsync()).Run.Checkpoint!.CompletedTraces.Single();
        var ids = f.Conversation.PendingAdviceIds.ToArray();
        await f.Session.ResolveAsync(ids[1], ApprovalDecision.Reject);
        var partial = await f.GetEvidenceAsync(0);
        Assert.IsFalse(partial.Valid);
        Assert.AreEqual(1, partial.Tools.Count);
        await f.Session.ResolveAsync(ids[0], ApprovalDecision.Execute);
        partial = await f.GetEvidenceAsync(0);
        Assert.AreEqual(3, partial.Tools.Count); // Original read, approved write, newly generated read.
        Assert.AreEqual(first.Duration, partial.Tools[0].Elapsed);
        ids = f.Conversation.PendingAdviceIds.ToArray();
        await f.Session.ResolveAsync(ids[1], ApprovalDecision.Reject);
        partial = await f.GetEvidenceAsync(0);
        Assert.AreEqual(3, partial.Tools.Count);
        await f.Session.ResolveAsync(ids[0], ApprovalDecision.Execute);
        var snapshot = await f.Session.GetRuntimeSnapshotAsync();
        var evidence = await f.GetEvidenceAsync(0);
        Assert.IsTrue(evidence.Valid);
        Assert.AreEqual(5, evidence.Tools.Count);
        Assert.AreEqual(5, evidence.Tools.Select(tool => tool.CallId).Distinct().Count());
        CollectionAssert.AreEqual(new[] { 1, 2, 3, 3, 4 }, evidence.Tools.Select(tool => tool.Iteration).ToArray());
        Assert.AreEqual(2, evidence.State["Resolutions"]!.Count(item => item["Outcome"]!.Value<string>() == "Rejected"));
        foreach (var tool in evidence.Tools)
        {
            var trace = snapshot.ObservedTraces!.Single(pair => $"{pair.Key.Batch}:{pair.Key.CallId}" == tool.CallId).Value;
            Assert.AreEqual(trace.Duration, tool.Elapsed);
            Assert.AreEqual(trace.Outcome, tool.Outcome);
        }
        Assert.AreEqual("Final document", f.Stored());
        await f.Session.ContinueAsync("trusted", "next");
        await f.Session.ResolveAsync(f.Conversation.PendingAdviceIds.Single(), ApprovalDecision.Execute);
        var next = await f.GetEvidenceAsync(1);
        Assert.IsTrue(next.Valid);
        Assert.AreEqual(1, next.Tools.Count);
        Assert.IsFalse(evidence.Tools.Any(tool => tool.CallId == next.Tools.Single().CallId));
        Assert.AreEqual(1, (await f.Session.GetRuntimeSnapshotAsync()).ObservedTraces!.Count);
    }

    [TestMethod]
    public async Task Evidence_UnavailableTool_IsNotProofOfHandlerDispatch()
    {
        using var f = new Fixture(Calls(ClaudeContentBlock.ToolUse("missing", "Unavailable", new())), Done());
        await f.Session.StartAsync();
        var evidence = await f.GetEvidenceAsync(0);
        Assert.IsFalse(evidence.Valid);
        Assert.AreEqual(0, evidence.Tools.Count);
        Assert.AreEqual(0, f.Dispatches);
    }

    [TestMethod]
    public async Task Evidence_IsConsumableByPublishedAssertionEvaluator()
    {
        using var f = new Fixture(Calls(Write("w", "Original", "Edited")), Done());
        await f.Session.StartAsync();
        await f.Session.ResolveAsync(f.Conversation.PendingAdviceIds.Single(), ApprovalDecision.Execute);
        var evidence = await f.GetEvidenceAsync(0);
        var evaluationCase = new EvaluationCase("offline-edit", 1,
        [
            new EvaluationStep(0, null, null, new EvaluationExpectation(
            [
                new EvaluationAssertion("document", EvaluationAssertionKinds.State, "correctness", 1, 0,
                    true, false, new JObject { ["DocumentContent"] = "Edited document" }),
                // Tool assertions match name/parameters/result/count, not Outcome. Check
                // canonical dispatch Outcome separately through the resolution state.
                new EvaluationAssertion("write", EvaluationAssertionKinds.Tool, "correctness", 1, 0,
                    true, false, new JObject { ["name"] = "ReplaceText", ["minCount"] = 1, ["maxCount"] = 1 }),
                new EvaluationAssertion("outcome", EvaluationAssertionKinds.State, "correctness", 1, 0,
                    true, false, new JObject { ["Resolutions"] = new JArray(new JObject
                        { ["CallId"] = "w", ["Name"] = "ReplaceText", ["Outcome"] = "Succeeded" }) }),
                new EvaluationAssertion("count", EvaluationAssertionKinds.MaxToolCalls, "correctness", 1, 0,
                    true, false, new JValue(1))
            ]))
        ]);
        var evaluator = new AssertionEvaluator();
        var result = evaluator.Evaluate(evaluationCase, new EvaluationEvidence([evidence]));
        Assert.IsTrue(result.Valid);
        Assert.IsTrue(result.Passed);
        Assert.AreEqual(100d, result.Score);
        Assert.IsTrue(result.Assertions.All(assertion => assertion.Matched));
        // A wrong expected document actually fails; this exercises matching, not just DTO shape.
        evidence.State["DocumentContent"] = "Wrong document";
        result = evaluator.Evaluate(evaluationCase, new EvaluationEvidence([evidence]));
        Assert.IsTrue(result.Valid);
        Assert.IsFalse(result.Passed);
        Assert.IsFalse(result.Assertions.Single(assertion => assertion.Id == "document").Matched);
    }

    private static ClaudeContentBlock Read(string id) => ClaudeContentBlock.ToolUse(id, "ReadFullDocument", new());
    private static ClaudeContentBlock Write(string id, string oldText, string newText) =>
        ClaudeContentBlock.ToolUse(id, "ReplaceText", new() { ["oldString"] = oldText, ["newString"] = newText });
    private static ClaudeResponse Calls(params ClaudeContentBlock[] calls) => new()
    {
        ReasoningContent = "batch thought", Content = [ClaudeContentBlock.TextBlock("proposal"), .. calls]
    };
    private static ClaudeResponse Done() => new() { ReasoningContent = "final thought", Content = [ClaudeContentBlock.TextBlock("done")] };

    private sealed class FakeClient(IServiceScopeFactory scopes, ClaudeResponse[] responses)
        : ClaudeClient(scopes, NullLogger<ClaudeClient>.Instance)
    {
        public List<List<ClaudeMessage>> Requests { get; } = [];
        public Exception? Error { get; set; }
        public override Task<ClaudeResponse> SendAsync(string systemPrompt, List<ClaudeMessage> messages,
            List<ClaudeTool>? tools, CancellationToken ct = default, int maxTokens = 4096)
        {
            Requests.Add(messages);
            return Error == null ? Task.FromResult(responses[Requests.Count - 1]) : Task.FromException<ClaudeResponse>(Error);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        public AgentConversation Conversation { get; } = new()
        {
            UserId = "trusted", DocumentId = Guid.NewGuid(), DocumentContent = "Original document",
            Messages = [new ToolMessagesItem { Role = "system", Content = "system" }, new ToolMessagesItem { Role = "user", Content = "edit" }]
        };
        public AdviceService Advice { get; } = new();
        public FakeClient Client { get; }
        public AgentSessionFacade Session { get; }
        public Action? OnDbResolve { get; set; }
        public int Dispatches { get; private set; }
        public Fixture(params ClaudeResponse[] responses) : this(null, responses) { }
        public Fixture(List<ToolMessagesItem>? history, params ClaudeResponse[] responses)
        {
            if (history != null) Conversation.Messages = history;
            var services = new ServiceCollection();
            services.AddScoped<CurrentUserService>();
            services.AddScoped<DocumentReadTools>();
            var database = Guid.NewGuid().ToString();
            services.AddDbContext<InMemoryContext>(options => options.UseInMemoryDatabase(database));
            services.AddScoped<TemplateDbContext>(sp =>
            {
                OnDbResolve?.Invoke();
                Dispatches++;
                return sp.GetRequiredService<InMemoryContext>();
            });
            _provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            using (var scope = _provider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<InMemoryContext>();
                db.MarkdownDocuments.Add(new MarkdownDocument { Id = Conversation.DocumentId, UserId = Conversation.UserId, Content = Conversation.DocumentContent });
                db.SaveChanges();
            }
            // Count persistence dispatches independently of verification reads.
            OnDbResolve = null;
            Client = new FakeClient(_provider.GetRequiredService<IServiceScopeFactory>(), responses);
            Session = new AgentSessionFacade(Conversation, Client, _provider.GetRequiredService<IServiceScopeFactory>(), new ToolRegistry(_provider), Advice);
        }
        public async Task<EvaluationStepEvidence> GetEvidenceAsync(int stepIndex) =>
            AgentSessionEvidenceAdapter.ToStepEvidence(stepIndex, await Session.GetRuntimeSnapshotAsync());

        public string Stored()
        {
            using var scope = _provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<InMemoryContext>().MarkdownDocuments.AsNoTracking().Single().Content!;
        }
        public void Dispose() => _provider.Dispose();
    }
}
