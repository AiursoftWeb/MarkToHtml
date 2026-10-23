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
