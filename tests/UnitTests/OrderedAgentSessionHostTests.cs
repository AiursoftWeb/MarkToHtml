using Aiursoft.AgentKit;
using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Messages;
using Aiursoft.MarkToHtml.Services.Agent;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class OrderedAgentSessionHostTests
{
    [TestMethod]
    public async Task MixedBatch_ReadsExecuteInOrder_WritesWait_OnlyFinalApprovalResumesModel()
    {
        using var fixture = new Fixture(Calls(Read("r1"), Write("w1"), Read("r2"), Write("w2")), Done());
        var paused = await fixture.Host.StartAsync();

        CollectionAssert.AreEqual(new[] { "r1", "r2" }, fixture.Executed.ToArray());
        Assert.AreEqual(OrderedAgentRunOutcome.AwaitingApproval, paused.Outcome);
        Assert.AreEqual(1, fixture.Model.Requests.Count);
        Assert.AreEqual(0, paused.Results.Count); // Partial results belong to the checkpoint.
        CollectionAssert.AreEqual(new[] { "r1", "r2" }, paused.Checkpoint!.CompletedResults.Select(x => x.CallId).ToArray());
        Assert.AreEqual(2, paused.Checkpoint.CompletedTraces.Count);
        Assert.AreEqual(0, paused.Transcript.Count(x => x.Role == TranscriptRole.Tool));

        var partial = await fixture.Host.ResolveAsync("w1", ApprovalDecision.Execute);
        CollectionAssert.AreEqual(new[] { "r1", "r2", "w1" }, fixture.Executed.ToArray());
        Assert.AreEqual(OrderedAgentRunOutcome.AwaitingApproval, partial.Outcome);
        Assert.AreEqual(1, fixture.Model.Requests.Count);
        Assert.AreEqual(3, partial.Checkpoint!.CompletedResults.Count);
        Assert.AreEqual(3, partial.Checkpoint.CompletedTraces.Count);

        var completed = await fixture.Host.ResolveAsync("w2", ApprovalDecision.Execute);
        Assert.AreEqual(OrderedAgentRunOutcome.Completed, completed.Outcome);
        Assert.AreEqual("done", completed.FinalText);
        Assert.IsNull(completed.Checkpoint);
        Assert.AreEqual(2, fixture.Model.Requests.Count);
        CollectionAssert.AreEqual(new[] { "r1", "w1", "r2", "w2" }, completed.Results.Select(x => x.CallId).ToArray());
        var batch = fixture.Model.Requests[1].Transcript.Single(x => x.Role == TranscriptRole.Tool);
        CollectionAssert.AreEqual(new[] { "r1", "w1", "r2", "w2" }, batch.ToolResults.Select(x => x.CallId).ToArray());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Host.StartAsync());
    }

    [TestMethod]
    public async Task FinalRejection_ResumesModelWithoutExecutingRejectedWrite()
    {
        using var fixture = new Fixture(Calls(Write("w1"), Write("w2")), Done());
        await fixture.Host.StartAsync();
        await fixture.Host.ResolveAsync("w1", ApprovalDecision.Execute);
        var completed = await fixture.Host.ResolveAsync("w2", ApprovalDecision.Reject);

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
    public async Task InvalidAndRepeatedResolutions_DoNotChangeCheckpointOrExecuteAgain()
    {
        using var fixture = new Fixture(Calls(Write("w1"), Write("w2")), Done());
        var before = await fixture.Host.StartAsync();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Host.ResolveAsync("unknown", ApprovalDecision.Execute));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Host.ResolveAsync("", ApprovalDecision.Execute));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => fixture.Host.ResolveAsync("w1", ApprovalDecision.Defer));
        AssertSnapshotEqual(before, (await fixture.Host.GetSnapshotAsync())!);
        Assert.AreEqual(0, fixture.Executed.Count);

        var partial = await fixture.Host.ResolveAsync("w1", ApprovalDecision.Execute);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Host.ResolveAsync("w1", ApprovalDecision.Execute));
        AssertSnapshotEqual(partial, (await fixture.Host.GetSnapshotAsync())!);
        CollectionAssert.AreEqual(new[] { "w1" }, fixture.Executed.ToArray());
        Assert.AreEqual(1, fixture.Model.Requests.Count);
        await fixture.Host.ResolveAsync("w2", ApprovalDecision.Reject);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Host.ResolveAsync("w1", ApprovalDecision.Execute));
        CollectionAssert.AreEqual(new[] { "w1" }, fixture.Executed.ToArray());
    }

    [TestMethod]
    public async Task Snapshots_IsolateCheckpointResultsTranscriptAndConstructorInput()
    {
        using var fixture = new Fixture(Calls(Read("r1"), Write("w1"), Write("w2")), Done());
        Assert.IsNull(await fixture.Host.GetSnapshotAsync());
        fixture.Initial[0] = TranscriptMessage.User("changed after construction");
        var returned = await fixture.Host.StartAsync();
        Assert.AreEqual("initial", ((TextBlock)fixture.Model.Requests[0].Transcript[0].Content[0]).Text);
        var original = (await fixture.Host.GetSnapshotAsync())!;

        returned.Checkpoint!.Resolutions[1].Call.Arguments["id"] = "tampered";
        ((JValue)returned.Checkpoint.CompletedResults[0].Output!).Value = "tampered";
        ((ToolCallBlock)returned.Checkpoint.Transcript.Last().Content[0]).Call.Arguments["id"] = "tampered";
        ((ToolCallBlock)returned.Transcript.Last().Content[0]).Call.Arguments["id"] = "tampered";
        AssertSnapshotEqual(original, (await fixture.Host.GetSnapshotAsync())!);

        var partial = await fixture.Host.ResolveAsync("w1", ApprovalDecision.Execute);
        partial.Checkpoint!.Resolutions[1].Call.Arguments["id"] = "tampered again";
        var readback = (await fixture.Host.GetSnapshotAsync())!;
        readback.Checkpoint!.Resolutions[1].Call.Arguments["id"] = "tampered snapshot";
        await fixture.Host.ResolveAsync("w2", ApprovalDecision.Execute);
        CollectionAssert.AreEqual(new[] { "r1", "w1", "w2" }, fixture.Executed.ToArray());

        var completed = (await fixture.Host.GetSnapshotAsync())!;
        var expected = (await fixture.Host.GetSnapshotAsync())!;
        ((JValue)completed.Results[0].Output!).Value = "tampered final";
        ((ToolCallBlock)completed.Transcript[1].Content[0]).Call.Arguments["id"] = "tampered final";
        AssertSnapshotEqual(expected, (await fixture.Host.GetSnapshotAsync())!);
    }

    [TestMethod]
    public async Task Approvals_DoNotResetTenModelCallBudget()
    {
        using var fixture = new Fixture(Enumerable.Range(1, 10).Select(i => Calls(Write($"w{i}"))).ToArray());
        var result = await fixture.Host.StartAsync();
        for (var i = 1; i <= 10; i++)
        {
            Assert.AreEqual(i, result.Iterations);
            Assert.AreEqual(10, result.Checkpoint!.MaxIterations);
            Assert.AreEqual(i, fixture.Model.Requests.Count);
            result = await fixture.Host.ResolveAsync($"w{i}", ApprovalDecision.Execute);
        }

        Assert.AreEqual(OrderedAgentRunOutcome.MaxIterations, result.Outcome);
        Assert.AreEqual(10, result.Iterations);
        Assert.AreEqual(10, fixture.Model.Requests.Count);
        Assert.AreEqual(10, fixture.Executed.Count);
        Assert.AreEqual(10, result.Results.Count);
    }

    [TestMethod]
    public async Task ConcurrentApprovals_AreSerialized_AndDuplicateDoesNotExecuteTwice()
    {
        using var fixture = new Fixture(Calls(Write("w1"), Write("w2")), Done());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.WriteHandler = async id =>
        {
            if (id != "w1") return;
            entered.SetResult();
            await release.Task;
        };
        await fixture.Host.StartAsync();
        var first = fixture.Host.ResolveAsync("w1", ApprovalDecision.Execute);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var duplicate = fixture.Host.ResolveAsync("w1", ApprovalDecision.Execute);
        var second = fixture.Host.ResolveAsync("w2", ApprovalDecision.Execute);
        Assert.IsFalse(duplicate.IsCompleted);
        Assert.IsFalse(second.IsCompleted);
        Assert.AreEqual(1, fixture.Model.Requests.Count);
        CollectionAssert.AreEqual(new[] { "w1" }, fixture.Executed.ToArray());
        release.SetResult();

        await first;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => duplicate);
        Assert.AreEqual(OrderedAgentRunOutcome.Completed, (await second).Outcome);
        CollectionAssert.AreEqual(new[] { "w1", "w2" }, fixture.Executed.ToArray());
        Assert.AreEqual(2, fixture.Model.Requests.Count);
    }

    [TestMethod]
    public async Task ToolExceptions_UseRunnerRedaction_NotLegacyErrorDetails()
    {
        using var fixture = new Fixture(Calls(Write("w1")), Done());
        fixture.WriteHandler = _ => throw new InvalidOperationException("sensitive detail");
        await fixture.Host.StartAsync();
        var result = await fixture.Host.ResolveAsync("w1", ApprovalDecision.Execute);
        Assert.AreEqual(OrderedAgentRunOutcome.Completed, result.Outcome);
        Assert.AreEqual(ToolOutcome.Failed, result.Results.Single().Outcome);
        Assert.AreEqual("Tool execution failed.", result.Results.Single().Error);
        Assert.AreEqual(2, fixture.Model.Requests.Count);
    }

    [TestMethod]
    public async Task CancelledBeforeDispatch_CanRetry_ButCancelledAfterWrite_CannotReplayCheckpoint()
    {
        using var fixture = new Fixture(Calls(Write("w1"), Write("w2")), Done());
        var paused = await fixture.Host.StartAsync();
        using var beforeDispatch = new CancellationTokenSource();
        beforeDispatch.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() =>
            fixture.Host.ResolveAsync("w1", ApprovalDecision.Execute, beforeDispatch.Token));
        AssertSnapshotEqual(paused, (await fixture.Host.GetSnapshotAsync())!);
        Assert.AreEqual(0, fixture.Executed.Count);

        using var duringDispatch = new CancellationTokenSource();
        var dispatchToken = duringDispatch.Token;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.WriteHandler = async _ =>
        {
            entered.SetResult();
            await release.Task;
            dispatchToken.ThrowIfCancellationRequested();
        };
        var dispatch = fixture.Host.ResolveAsync("w1", ApprovalDecision.Execute, dispatchToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        duringDispatch.Cancel();
        release.SetResult();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => dispatch);
        Assert.IsNull(await fixture.Host.GetSnapshotAsync());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Host.ResolveAsync("w1", ApprovalDecision.Execute));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Host.ResolveAsync("w2", ApprovalDecision.Execute));
        CollectionAssert.AreEqual(new[] { "w1" }, fixture.Executed.ToArray());
        Assert.AreEqual(1, fixture.Model.Requests.Count);
    }

    private static void AssertSnapshotEqual(OrderedAgentRunResult expected, OrderedAgentRunResult actual) =>
        Assert.IsTrue(JToken.DeepEquals(JToken.FromObject(expected), JToken.FromObject(actual)));

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
        public OrderedAgentSessionHost Host { get; }
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
            Host = new OrderedAgentSessionHost(Initial, [read, write], Model);
        }

        public void Dispose() => _services.Dispose();
    }
}
