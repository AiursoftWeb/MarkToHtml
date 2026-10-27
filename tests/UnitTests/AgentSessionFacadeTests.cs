using Aiursoft.AgentKit;
using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Messages;
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
        Assert.AreEqual(AgentToolResultText.Rejected, f.Advice.Get(ids[1])!.Result);
        Assert.AreEqual("Edited document", f.Stored());
        Assert.AreEqual("final thought", f.Conversation.Messages.Last().ReasoningContent);
        var replay = f.Client.Requests[1];
        Assert.AreEqual("batch thought", replay.Single(m => m.Role == "assistant").ReasoningContent);
        Assert.IsTrue(replay.SelectMany(m => m.Content as List<ClaudeContentBlock> ?? []).Any(b =>
            b.ToolUseId == "w2" && Equals(b.Content, AgentToolResultText.Rejected)));
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
        public Fixture(params ClaudeResponse[] responses)
        {
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
        public string Stored()
        {
            using var scope = _provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<InMemoryContext>().MarkdownDocuments.AsNoTracking().Single().Content!;
        }
        public void Dispose() => _provider.Dispose();
    }
}
