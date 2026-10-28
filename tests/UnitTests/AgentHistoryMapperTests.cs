using Aiursoft.AgentKit;
using Aiursoft.AgentKit.Messages;
using Aiursoft.MarkToHtml.Services.Agent;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class AgentHistoryMapperTests
{
    [TestMethod]
    public void Import_PreservesSeparateSystemsMetaAbsoluteReasoningAndMultipleCalls()
    {
        var legacy = new List<ToolMessagesItem>
        {
            new() { Role = "system", Content = "first" },
            new() { Role = "system", Content = "second", IsMeta = true },
            new() { Role = "user", Content = "meta", IsMeta = true },
            new() { Role = "assistant", Content = " ", ReasoningContent = "hidden" },
            new() { Role = "assistant", Content = "calls", ReasoningContent = "thought", IsMeta = true,
                ToolCalls = [LegacyCall("a", "{\"nested\":{\"n\":1},\"array\":[true,null]}"), LegacyCall("b", "{} ")] },
            new() { Role = "tool", ToolCallId = "a", Content = "Error: not outcome evidence", IsMeta = true },
            new() { Role = "tool", ToolCallId = "b", Content = AgentToolResultMapper.Rejected }
        };
        var imported = AgentHistoryMapper.ImportCompletedLegacyHistoryLossy(legacy);
        Assert.AreEqual(7, imported.Transcript.Count);
        Assert.AreEqual(TranscriptRole.System, imported.Transcript[1].Role);
        Assert.IsTrue(imported.Transcript[1].IsMeta);
        Assert.IsTrue(imported.Transcript[2].IsMeta);
        Assert.AreEqual("hidden", imported.InitialReasoning[3]);
        Assert.AreEqual("thought", imported.InitialReasoning[4]);
        Assert.AreEqual(2, imported.Transcript[4].ToolCalls.Count);
        Assert.IsTrue(JToken.DeepEquals(JToken.Parse(legacy[4].ToolCalls![0].Function!.Arguments!), imported.Transcript[4].ToolCalls[0].Arguments));
        Assert.AreEqual(ToolOutcome.Succeeded, imported.Transcript[6].ToolResults[0].Outcome); // Lossy placeholder, not inferred rejection.
        var ui = AgentHistoryMapper.ProjectForUi(imported.Transcript, imported.InitialReasoning);
        Assert.AreEqual(6, ui.Count);
        Assert.AreEqual("thought", ui[3].ReasoningContent);
        Assert.IsTrue(ui[3].IsMeta);
        Assert.IsTrue(ui[4].IsMeta);
        CollectionAssert.AreEqual(new[] { "a", "b" }, ui.Where(x => x.Role == "tool").Select(x => x.ToolCallId).ToArray());
        ui[3].ToolCalls![0].Function!.Arguments = "{}";
        ui[4].Content = "changed projection";
        legacy[4].ToolCalls![0].Function!.Arguments = "{}";
        Assert.AreEqual(1, imported.Transcript[4].ToolCalls[0].Arguments["nested"]!["n"]!.Value<int>());
        Assert.AreEqual("Error: not outcome evidence", imported.Transcript[5].ToolResults[0].Output!.Value<string>());
        Assert.AreEqual(7, imported.Transcript.Count); // UI hiding never edits canonical history.
    }

    [TestMethod]
    public void Projection_UsesResultTextAndIsIndependentOfCanonicalJson()
    {
        var output = JObject.Parse("{\"nested\":[1,2]}");
        var transcript = new[] { TranscriptMessage.Tool([
            new ToolResult("a", "Read", ToolOutcome.Succeeded, output),
            new ToolResult("b", "Write", ToolOutcome.Rejected, Error: "Tool call rejected."),
            new ToolResult("c", "Read", ToolOutcome.Failed, Error: "Tool execution failed.")]) };
        var ui = AgentHistoryMapper.ProjectForUi(transcript, new Dictionary<int, string?>());
        Assert.AreEqual("{\"nested\":[1,2]}", ui[0].Content);
        Assert.AreEqual(AgentToolResultMapper.Rejected, ui[1].Content);
        Assert.AreEqual("Tool execution failed.", ui[2].Content);
        ui[0].Content = "changed";
        Assert.AreEqual(1, transcript[0].ToolResults[0].Output!["nested"]![0]!.Value<int>());
        output["nested"] = "changed source";
        Assert.AreEqual(1, transcript[0].ToolResults[0].Output!["nested"]![0]!.Value<int>());
    }

    [TestMethod]
    public void Import_MalformedArgumentsRetainLegacyEmptyObjectFallback()
    {
        foreach (var args in new[] { "{broken", "[]", "null", "123" })
        {
            var imported = AgentHistoryMapper.ImportCompletedLegacyHistoryLossy([
                new() { Role = "assistant", ToolCalls = [LegacyCall("a", args)] },
                new() { Role = "tool", ToolCallId = "a", Content = "done" }]);
            Assert.IsTrue(JToken.DeepEquals(new JObject(), imported.Transcript[0].ToolCalls[0].Arguments));
        }
    }

    [TestMethod]
    public void Import_RejectsMissingIdsNamesOrIncompleteHistoryWithoutInventingIds()
    {
        foreach (var id in new[] { null, "", " " })
        {
            var exception = Assert.ThrowsExactly<NotSupportedException>(() => AgentHistoryMapper.ImportCompletedLegacyHistoryLossy([
                new() { Role = "assistant", ToolCalls = [LegacyCall(id, "{}")] }]));
            StringAssert.Contains(exception.Message, "never synthesized");
            StringAssert.Contains(exception.Message, "not enabled in production");
            Assert.ThrowsExactly<NotSupportedException>(() => AgentHistoryMapper.ImportCompletedLegacyHistoryLossy([
                new() { Role = "tool", ToolCallId = id }]));
        }
        var invalid = LegacyCall("a", "{}");
        invalid.Function!.Name = " ";
        Assert.ThrowsExactly<NotSupportedException>(() => AgentHistoryMapper.ImportCompletedLegacyHistoryLossy([
            new() { Role = "assistant", ToolCalls = [invalid] }]));
        Assert.ThrowsExactly<ArgumentException>(() => AgentHistoryMapper.ImportCompletedLegacyHistoryLossy([
            new() { Role = "assistant", ToolCalls = [LegacyCall("a", "{}")] }]));
        Assert.ThrowsExactly<ArgumentException>(() => AgentHistoryMapper.ImportCompletedLegacyHistoryLossy([
            new() { Role = "tool", ToolCallId = "orphan" }]));
    }

    [TestMethod]
    public void Projection_RejectsReasoningAtNonAssistantIndex()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AgentHistoryMapper.ProjectForUi(
            [TranscriptMessage.User("same text")], new Dictionary<int, string?> { [0] = "not associated" }));
    }

    [TestMethod]
    public async Task OrderedHost_PartialReadAndApprovedResultsProjectInCallOrder_FinalHasNoDuplicates()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var registry = new ToolRegistry(services);
        var read = AgentToolAdapter.ForReadOnly(registry.GetTool("ReadFullDocument")!, (_, _) => ValueTask.FromResult("read"));
        var write = AgentToolAdapter.ForApprovedWrite(registry.GetTool("ReplaceText")!, (_, _) => ValueTask.FromResult("wrote"));
        var model = new ScriptedModel();
        var host = new OrderedAgentSessionHost([TranscriptMessage.System("system"), TranscriptMessage.User("go")], [read, write], model);
        var reason = new Dictionary<int, string?> { [2] = "batch" };
        var paused = await host.StartAsync();
        var pausedUi = AgentHistoryMapper.ProjectForUi(paused, reason);
        CollectionAssert.AreEqual(new[] { "r1", "r2" }, ResultIds(pausedUi));
        Assert.AreEqual("batch", pausedUi[2].ReasoningContent);
        Assert.AreEqual(0, paused.Transcript.Count(x => x.Role == TranscriptRole.Tool));
        Assert.AreEqual(2, paused.Checkpoint!.CompletedResults.Count);
        var partial = await host.ResolveAsync("w1", ApprovalDecision.Execute);
        CollectionAssert.AreEqual(new[] { "r1", "w1", "r2" }, ResultIds(AgentHistoryMapper.ProjectForUi(partial, reason)));
        Assert.AreEqual(0, partial.Transcript.Count(x => x.Role == TranscriptRole.Tool));
        var complete = await host.ResolveAsync("w2", ApprovalDecision.Reject);
        reason[4] = "final empty assistant";
        var completeUi = AgentHistoryMapper.ProjectForUi(complete, reason);
        CollectionAssert.AreEqual(new[] { "r1", "w1", "r2", "w2" }, ResultIds(completeUi));
        Assert.AreEqual(AgentToolResultMapper.Rejected, completeUi.Last().Content);
        Assert.AreEqual(5, complete.Transcript.Count);
        Assert.AreEqual(7, completeUi.Count); // Empty final assistant hidden; four individual UI results.
        Assert.AreEqual(2, model.Requests);
        Assert.AreEqual(4, complete.Transcript[3].ToolResults.Count);
        CollectionAssert.AreEqual(new[] { "r1", "r2" }, ResultIds(AgentHistoryMapper.ProjectForUi(paused, new Dictionary<int, string?> { [2] = "batch" })));
    }

    private static string?[] ResultIds(IEnumerable<ToolMessagesItem> items) => items.Where(x => x.Role == "tool").Select(x => x.ToolCallId).ToArray();
    private static ToolCallData LegacyCall(string? id, string args) => new()
    {
        Id = id, Function = new ToolCallFunction { Name = "Read", Arguments = args }
    };

    private sealed class ScriptedModel : IAgentModelClient
    {
        public int Requests { get; private set; }
        public ValueTask<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Requests == 1)
                return ValueTask.FromResult(new AgentModelResponse(new[] { Call("r1", "ReadFullDocument"), Call("w1", "ReplaceText"), Call("r2", "ReadFullDocument"), Call("w2", "ReplaceText") }, AgentFinishReason.ToolCalls));
            Assert.AreEqual(4, request.Transcript.Last().ToolResults.Count);
            return ValueTask.FromResult(new AgentModelResponse([new TextBlock(" ")]));
        }
        private static ToolCallBlock Call(string id, string name) => new(new ToolCall(id, name, new JObject()));
    }
}
