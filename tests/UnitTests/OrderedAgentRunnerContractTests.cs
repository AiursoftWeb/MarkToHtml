using Aiursoft.AgentKit;
using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Messages;
using Aiursoft.MarkToHtml.Services.Agent;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class OrderedAgentRunnerContractTests
{
    [TestMethod]
    public async Task MixedBatch_ReadsExecuteInOrder_WritesWait_OnlyFinalApprovalResumesModel()
    {
        using var fixture = new Fixture(Calls(Read("r1"), Write("w1"), Read("r2"), Write("w2")), Done());
        var paused = await fixture.Runner.RunAsync(fixture.Request);

        CollectionAssert.AreEqual(new[] { "r1", "r2" }, fixture.Executed.ToArray());
        Assert.AreEqual(OrderedAgentRunOutcome.AwaitingApproval, paused.Outcome);
        Assert.AreEqual(1, fixture.Model.Requests.Count);
        Assert.AreEqual(0, paused.Results.Count); // Partial results belong to the checkpoint.
        CollectionAssert.AreEqual(new[] { "r1", "r2" }, paused.Checkpoint!.CompletedResults.Select(x => x.CallId).ToArray());
        Assert.AreEqual(2, paused.Checkpoint.CompletedTraces.Count);
        Assert.AreEqual(0, paused.Transcript.Count(x => x.Role == TranscriptRole.Tool));

        var partial = await fixture.Runner.ResolveAsync(paused.Checkpoint!, fixture.Tools, "w1", ApprovalDecision.Execute);
        CollectionAssert.AreEqual(new[] { "r1", "r2", "w1" }, fixture.Executed.ToArray());
        Assert.AreEqual(OrderedAgentRunOutcome.AwaitingApproval, partial.Outcome);
        Assert.AreEqual(1, fixture.Model.Requests.Count);
        Assert.AreEqual(3, partial.Checkpoint!.CompletedResults.Count);
        Assert.AreEqual(3, partial.Checkpoint.CompletedTraces.Count);

        var completed = await fixture.Runner.ResolveAsync(partial.Checkpoint!, fixture.Tools, "w2", ApprovalDecision.Execute);
        Assert.AreEqual(OrderedAgentRunOutcome.Completed, completed.Outcome);
        Assert.AreEqual("done", completed.FinalText);
        Assert.IsNull(completed.Checkpoint);
        Assert.AreEqual(2, fixture.Model.Requests.Count);
        CollectionAssert.AreEqual(new[] { "r1", "w1", "r2", "w2" }, completed.Results.Select(x => x.CallId).ToArray());
        var batch = fixture.Model.Requests[1].Transcript.Single(x => x.Role == TranscriptRole.Tool);
        CollectionAssert.AreEqual(new[] { "r1", "w1", "r2", "w2" }, batch.ToolResults.Select(x => x.CallId).ToArray());
    }

    [TestMethod]
    public async Task FinalRejection_ResumesModelWithoutExecutingRejectedWrite()
    {
        using var fixture = new Fixture(Calls(Write("w1"), Write("w2")), Done());
        var paused = await fixture.Runner.RunAsync(fixture.Request);
        var partial = await fixture.Runner.ResolveAsync(paused.Checkpoint!, fixture.Tools, "w1", ApprovalDecision.Execute);
        var completed = await fixture.Runner.ResolveAsync(partial.Checkpoint!, fixture.Tools, "w2", ApprovalDecision.Reject);

        CollectionAssert.AreEqual(new[] { "w1" }, fixture.Executed.ToArray());
        Assert.AreEqual(2, fixture.Model.Requests.Count);
        Assert.AreEqual(OrderedAgentRunOutcome.Completed, completed.Outcome);
        var rejected = completed.Results.Single(x => x.CallId == "w2");
        Assert.AreEqual(ToolOutcome.Rejected, rejected.Outcome);
        // Canonical evidence stays generic; ClaudeAgentModelClient projects legacy text at the wire boundary.
        Assert.AreEqual("Tool call rejected.", rejected.Error);
        Assert.AreEqual(ToolOutcome.Rejected, fixture.Model.Requests[1].Transcript
            .Single(x => x.Role == TranscriptRole.Tool).ToolResults[1].Outcome);
    }

    [TestMethod]
    public async Task Approvals_DoNotResetTenModelCallBudget()
    {
        using var fixture = new Fixture(Enumerable.Range(1, 10).Select(i => Calls(Write($"w{i}"))).ToArray());
        var result = await fixture.Runner.RunAsync(fixture.Request);
        for (var i = 1; i <= 10; i++)
        {
            Assert.AreEqual(i, result.Iterations);
            Assert.AreEqual(10, result.Checkpoint!.MaxIterations);
            Assert.AreEqual(i, fixture.Model.Requests.Count);
            result = await fixture.Runner.ResolveAsync(result.Checkpoint!, fixture.Tools, $"w{i}", ApprovalDecision.Execute);
        }

        Assert.AreEqual(OrderedAgentRunOutcome.MaxIterations, result.Outcome);
        Assert.AreEqual(10, result.Iterations);
        Assert.AreEqual(10, fixture.Model.Requests.Count);
        Assert.AreEqual(10, fixture.Executed.Count);
        Assert.AreEqual(10, result.Results.Count);
    }

    [TestMethod]
    public async Task ToolExceptions_UseRunnerRedaction_NotLegacyErrorDetails()
    {
        using var fixture = new Fixture(Calls(Write("w1")), Done());
        fixture.WriteHandler = _ => throw new InvalidOperationException("sensitive detail");
        var paused = await fixture.Runner.RunAsync(fixture.Request);
        var result = await fixture.Runner.ResolveAsync(paused.Checkpoint!, fixture.Tools, "w1", ApprovalDecision.Execute);
        Assert.AreEqual(OrderedAgentRunOutcome.Completed, result.Outcome);
        Assert.AreEqual(ToolOutcome.Failed, result.Results.Single().Outcome);
        Assert.AreEqual("Tool execution failed.", result.Results.Single().Error);
        Assert.AreEqual(2, fixture.Model.Requests.Count);
    }

    [TestMethod]
    public async Task Cancellation_BeforeDispatchDoesNotExecute_AfterWriteDoesNotProduceResumableResult()
    {
        using var fixture = new Fixture(Calls(Write("w1"), Write("w2")), Done());
        var paused = await fixture.Runner.RunAsync(fixture.Request);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
            fixture.Runner.ResolveAsync(paused.Checkpoint!, fixture.Tools, "w1", ApprovalDecision.Execute, cancellation.Token).AsTask());
        Assert.AreEqual(0, fixture.Executed.Count);

        using var duringDispatch = new CancellationTokenSource();
        var dispatchToken = duringDispatch.Token;
        Action cancelDuringDispatch = duringDispatch.Cancel;
        fixture.WriteHandler = _ =>
        {
            cancelDuringDispatch();
            dispatchToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        };
        // A caller must not retry the old checkpoint after this exception. Production has
        // no in-flight cancellation API; facade failure/no-replay is tested at its boundary.
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            fixture.Runner.ResolveAsync(paused.Checkpoint!, fixture.Tools, "w1", ApprovalDecision.Execute, duringDispatch.Token).AsTask());
        CollectionAssert.AreEqual(new[] { "w1" }, fixture.Executed.ToArray());
        Assert.AreEqual(1, fixture.Model.Requests.Count);
    }

    private static ToolCallBlock Read(string id) => Call(id, "ReadFullDocument");
    private static ToolCallBlock Write(string id) => Call(id, "ReplaceText");
    private static ToolCallBlock Call(string id, string name) => new(new ToolCall(id, name, new JObject { ["id"] = id }));
    private static AgentModelResponse Calls(params ToolCallBlock[] calls) => new(calls, AgentFinishReason.ToolCalls);
    private static AgentModelResponse Done() => new([new TextBlock("done")]);

    private sealed class ScriptedModel(params AgentModelResponse[] responses) : IAgentModelClient
    {
        public List<AgentModelRequest> Requests { get; } = [];

        public ValueTask<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            Assert.IsTrue(Requests.Count <= responses.Length, "Unexpected extra model call.");
            return ValueTask.FromResult(responses[Requests.Count - 1]);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
        public List<string> Executed { get; } = [];
        public Func<string, Task>? WriteHandler { get; set; }
        public ScriptedModel Model { get; }
        public OrderedAgentRunner Runner { get; }
        public IAgentTool[] Tools { get; }
        public AgentRunRequest Request { get; }
        public TranscriptMessage[] Initial { get; } = [TranscriptMessage.User("initial")];

        public Fixture(params AgentModelResponse[] responses)
        {
            Model = new ScriptedModel(responses);
            var registry = new ToolRegistry(_services);
            var read = AgentToolAdapter.ForReadOnly(registry.GetTool("ReadFullDocument")!, (args, _) =>
            {
                var id = args["id"]!.Value<string>()!;
                Executed.Add(id);
                return ValueTask.FromResult($"read {id}");
            });
            var write = AgentToolAdapter.ForApprovedWrite(registry.GetTool("ReplaceText")!, async (args, _) =>
            {
                var id = args["id"]!.Value<string>()!;
                Executed.Add(id);
                if (WriteHandler is not null) await WriteHandler(id);
                return $"wrote {id}";
            });
            Tools = [read, write];
            Runner = new OrderedAgentRunner(Model);
            Request = new AgentRunRequest(Initial, Tools, new AgentRunOptions(MaxIterations: 10));
        }

        public void Dispose() => _services.Dispose();
    }
}
