using Aiursoft.AgentKit;
using Aiursoft.AgentKit.Messages;
using Aiursoft.MarkToHtml.Services.Agent;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class ClaudeAgentModelClientTests
{
    [TestMethod]
    public async Task Request_PreservesFirstSystemMetaUserAssistantAndIndividualResults()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = CreateClient(services);
        var adapter = new ClaudeAgentModelClient(client, [], 1234, new Dictionary<int, string?> { [4] = "reasoning" });
        var request = new AgentModelRequest([
            TranscriptMessage.System("first"), TranscriptMessage.System("ignored"),
            new TranscriptMessage(TranscriptRole.User, [new TextBlock("meta")], true),
            TranscriptMessage.User("question"),
            TranscriptMessage.Assistant([new TextBlock("answer"),
                new ToolCallBlock(new ToolCall("a", "Read", JObject.Parse("{\"nested\":{\"x\":1}}"))),
                new ToolCallBlock(new ToolCall("b", "Read", new JObject()))]),
            TranscriptMessage.Tool([
                new ToolResult("a", "Read", ToolOutcome.Succeeded, new JValue("one")),
                new ToolResult("b", "Read", ToolOutcome.Failed, Error: "two")])], []);

        await adapter.CompleteAsync(request, default);

        Assert.AreEqual("first", client.System);
        Assert.AreEqual(1234, client.MaxTokens);
        Assert.AreEqual(5, client.Messages.Count);
        Assert.AreEqual("meta", client.Messages[0].Content);
        Assert.AreEqual("question", client.Messages[1].Content);
        Assert.AreEqual("reasoning", client.Messages[2].ReasoningContent);
        var blocks = Blocks(client.Messages[2]);
        CollectionAssert.AreEqual(new[] { "text", "tool_use", "tool_use" }, blocks.Select(b => b.Type).ToArray());
        Assert.AreEqual("answer", blocks[0].Text);
        Assert.AreEqual("a", blocks[1].Id);
        Assert.AreEqual(1, ((JObject)blocks[1].Input!["nested"]!)["x"]!.Value<int>());
        Assert.AreEqual("a", Blocks(client.Messages[3]).Single().ToolUseId);
        Assert.AreEqual("one", Blocks(client.Messages[3]).Single().Content);
        Assert.AreEqual("b", Blocks(client.Messages[4]).Single().ToolUseId);
        Assert.AreEqual("two", Blocks(client.Messages[4]).Single().Content);
    }

    [TestMethod]
    public async Task Tools_UseExplicitRegistryOrderAndIndependentSchemas()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = CreateClient(services);
        var order = new[] { "Z", "A" };
        var adapter = new ClaudeAgentModelClient(client, order);
        order[0] = "changed";
        var request = new AgentModelRequest([], [new ToolDefinition("Z", "last", JObject.Parse("{\"type\":\"object\"}")), new ToolDefinition("A", "first")]);
        CollectionAssert.AreEqual(new[] { "A", "Z" }, request.Tools.Select(t => t.Name).ToArray());

        await adapter.CompleteAsync(request, default);

        CollectionAssert.AreEqual(new[] { "Z", "A" }, client.Tools!.Select(t => t.Name).ToArray());
        Assert.AreEqual("last", client.Tools![0].Description);
        Assert.AreEqual("object", ((System.Text.Json.JsonElement)client.Tools[0].InputSchema).GetProperty("type").GetString());
        client.Tools[0].InputSchema = new { changed = true };
        Assert.AreEqual("object", request.Tools.Single(t => t.Name == "Z").InputSchema["type"]!.Value<string>());
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("max_tokens")]
    [DataRow("refusal")]
    [DataRow("tool_use")]
    public async Task TextMapping_JoinsTextDropsUnknownAndIgnoresStopReason(string? stopReason)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = CreateClient(services);
        client.Response = new ClaudeResponse
        {
            StopReason = stopReason, ReasoningContent = "private",
            Content = [ClaudeContentBlock.TextBlock("one"), new ClaudeContentBlock { Type = "unknown", Text = "discard" }, ClaudeContentBlock.TextBlock("two")]
        };
        var completion = await new ClaudeAgentModelClient(client, []).CompleteAsync(new AgentModelRequest([], []), default);
        Assert.AreEqual("one\ntwo", completion.Text);
        Assert.AreEqual(AgentFinishReason.Stop, completion.FinishReason);
        Assert.HasCount(1, completion.Content);
        Assert.AreEqual("", client.System);
        Assert.AreEqual(4096, client.MaxTokens);
    }

    [TestMethod]
    public async Task ToolMapping_PreservesIdentityNestedJsonOrderAndDoesNotAlias()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = CreateClient(services);
        client.Response = new ClaudeResponse { StopReason = "end_turn", Content = [
            ClaudeContentBlock.ToolUse("two", "Z", new() { ["x"] = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("{\"items\":[1,true,null,\"text\"]}") }),
            ClaudeContentBlock.ToolUse("one", "A", new())] };
        var adapter = new ClaudeAgentModelClient(client, []);
        var response = await adapter.CompleteAsync(new AgentModelRequest([], []), default);
        Assert.AreEqual(AgentFinishReason.ToolCalls, response.FinishReason);
        CollectionAssert.AreEqual(new[] { "two", "one" }, response.ToolCalls.Select(c => c.Id).ToArray());
        Assert.IsTrue(JToken.DeepEquals(JObject.Parse("{\"x\":{\"items\":[1,true,null,\"text\"]}}"), response.ToolCalls[0].Arguments));
        client.Response.Content[0].Input!["x"] = "changed";
        Assert.IsInstanceOfType<JObject>(response.ToolCalls[0].Arguments["x"]);

        var request = new AgentModelRequest([TranscriptMessage.Assistant(response.Content)], []);
        await adapter.CompleteAsync(request, default);
        Blocks(client.Messages.Single())[0].Input!["x"] = "transport mutation";
        Assert.IsInstanceOfType<JObject>(request.Transcript.Single().ToolCalls[0].Arguments["x"]);
    }

    [TestMethod]
    public async Task Reasoning_ReplaysAutomaticallyAcrossCompletionsAndIsolatedSessions()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = CreateClient(services);
        var adapter = new ClaudeAgentModelClient(client, []);
        client.Response = new ClaudeResponse { ReasoningContent = "first reasoning", Content = [ClaudeContentBlock.TextBlock("same")] };
        var history = new List<TranscriptMessage> { TranscriptMessage.System("system"), TranscriptMessage.User("question") };
        var first = await adapter.CompleteAsync(new AgentModelRequest(history, []), default);
        history.Add(TranscriptMessage.Assistant(first.Content));
        history.Add(TranscriptMessage.User("continue"));
        client.Response.ReasoningContent = "second reasoning";
        var second = await adapter.CompleteAsync(new AgentModelRequest(history, []), default);
        Assert.AreEqual("first reasoning", client.Messages[1].ReasoningContent);
        history.Add(TranscriptMessage.Assistant(second.Content));
        await adapter.CompleteAsync(new AgentModelRequest(history, []), default);
        Assert.AreEqual("first reasoning", client.Messages[1].ReasoningContent);
        Assert.AreEqual("second reasoning", client.Messages[3].ReasoningContent);
        await new ClaudeAgentModelClient(client, []).CompleteAsync(new AgentModelRequest(history, []), default);
        Assert.IsTrue(client.Messages.All(message => message.ReasoningContent == null));
    }

    [TestMethod]
    public async Task SameInputRetry_ReplacesPendingReasoningWithoutMisassociation()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = CreateClient(services);
        var adapter = new ClaudeAgentModelClient(client, []);
        var request = new AgentModelRequest([TranscriptMessage.User("question")], []);
        client.Response = new ClaudeResponse { ReasoningContent = "discarded", Content = [ClaudeContentBlock.TextBlock("old")] };
        await adapter.CompleteAsync(request, default);
        client.Response = new ClaudeResponse { ReasoningContent = "retry", Content = [ClaudeContentBlock.TextBlock("new")] };
        var retry = await adapter.CompleteAsync(request, default);
        Assert.IsNull(client.Messages.Single().ReasoningContent);
        await adapter.CompleteAsync(new AgentModelRequest([.. request.Transcript, TranscriptMessage.Assistant(retry.Content)], []), default);
        Assert.AreEqual("retry", client.Messages[1].ReasoningContent);
    }

    [TestMethod]
    public async Task EditedHistoryOrUnexpectedAssistant_RejectsBeforeTransport()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = CreateClient(services);
        var adapter = new ClaudeAgentModelClient(client, []);
        client.Response.Content = [ClaudeContentBlock.TextBlock("expected")];
        await adapter.CompleteAsync(new AgentModelRequest([TranscriptMessage.User("original")], []), default);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await adapter.CompleteAsync(new AgentModelRequest([TranscriptMessage.User("edited")], []), default));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await adapter.CompleteAsync(new AgentModelRequest([TranscriptMessage.User("original"), TranscriptMessage.Assistant([new TextBlock("wrong")])], []), default));
        Assert.AreEqual(1, client.Calls);
    }

    [TestMethod]
    public async Task ExceptionsAndCancellationToken_ArePassedThrough()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = CreateClient(services);
        var adapter = new ClaudeAgentModelClient(client, []);
        using var cts = new CancellationTokenSource();
        var error = new HttpRequestException("transport failure");
        client.Error = error;
        var actual = await Assert.ThrowsAsync<HttpRequestException>(async () => await adapter.CompleteAsync(new AgentModelRequest([], []), cts.Token));
        Assert.AreSame(error, actual);
        Assert.AreEqual(cts.Token, client.Token);
        client.Error = new OperationCanceledException(cts.Token);
        var cancelled = await Assert.ThrowsAsync<OperationCanceledException>(async () => await adapter.CompleteAsync(new AgentModelRequest([], []), cts.Token));
        Assert.AreSame(client.Error, cancelled);
    }

    [TestMethod]
    [DataRow(null, "Read")]
    [DataRow("", "Read")]
    [DataRow(" ", "Read")]
    [DataRow("valid", null)]
    public async Task UnrepresentableCalls_FailExplicitlyWithoutGeneratingIds(string? id, string? name)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = CreateClient(services);
        client.Response.Content = [new ClaudeContentBlock { Type = "tool_use", Id = id, Name = name }];
        var error = await Assert.ThrowsAsync<NotSupportedException>(async () => await new ClaudeAgentModelClient(client, []).CompleteAsync(new AgentModelRequest([], []), default));
        StringAssert.Contains(error.Message, "never synthesized");
    }

    [TestMethod]
    public async Task InvalidReplayIndexAndToolOrder_FailBeforeTransport()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = CreateClient(services);
        var request = new AgentModelRequest([TranscriptMessage.User("user")], []);
        await Assert.ThrowsAsync<ArgumentException>(async () => await new ClaudeAgentModelClient(client, [], initialReasoning: new Dictionary<int, string?> { [0] = "wrong role" }).CompleteAsync(request, default));
        await Assert.ThrowsAsync<ArgumentException>(async () => await new ClaudeAgentModelClient(client, [], initialReasoning: new Dictionary<int, string?> { [1] = "out of range" }).CompleteAsync(request, default));
        await Assert.ThrowsAsync<ArgumentException>(async () => await new ClaudeAgentModelClient(client, [], initialReasoning: new Dictionary<int, string?> { [-1] = "negative" }).CompleteAsync(request, default));
        await Assert.ThrowsAsync<ArgumentException>(async () => await new ClaudeAgentModelClient(client, ["missing"]).CompleteAsync(request, default));
        Assert.AreEqual(0, client.Calls);
    }

    [TestMethod]
    public async Task EmptyResponse_MapsToEmptyStop()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var response = await new ClaudeAgentModelClient(CreateClient(services), []).CompleteAsync(new AgentModelRequest([], []), default);
        Assert.HasCount(0, response.Content);
        Assert.AreEqual("", response.Text);
        Assert.AreEqual(AgentFinishReason.Stop, response.FinishReason);
    }

    [TestMethod]
    public async Task OrderedRejection_PreservesLegacyTransportTextAndCanonicalEvidence()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = CreateClient(services);
        client.Response = new ClaudeResponse
        {
            ReasoningContent = "original reasoning",
            Content =
            [
                ClaudeContentBlock.ToolUse("read", "ReadFullDocument", new()),
                ClaudeContentBlock.ToolUse("approve", "ReplaceText", new()),
                ClaudeContentBlock.ToolUse("reject", "ReplaceText", new())
            ]
        };
        var registry = new ToolRegistry(services);
        var writes = 0;
        var tools = new IAgentTool[]
        {
            AgentToolAdapter.ForReadOnly(registry.GetTool("ReadFullDocument")!,
                (_, _) => ValueTask.FromResult("document")),
            AgentToolAdapter.ForApprovedWrite(registry.GetTool("ReplaceText")!, (_, _) =>
            {
                writes++;
                return ValueTask.FromResult("updated");
            })
        };
        var adapter = new ClaudeAgentModelClient(client, tools.Select(tool => tool.Definition.Name));
        var host = new OrderedAgentSessionHost([TranscriptMessage.User("edit")], tools, adapter);
        var paused = await host.StartAsync();
        var pausedUi = AgentHistoryMapper.ProjectForUi(paused, adapter.GetReasoningSnapshot());
        Assert.AreEqual("original reasoning", pausedUi[1].ReasoningContent);
        Assert.AreEqual("read", pausedUi.Last().ToolCallId);
        await host.ResolveAsync("approve", ApprovalDecision.Execute);
        Assert.AreEqual(1, client.Calls);
        client.Response = new ClaudeResponse { ReasoningContent = "final reasoning", Content = [ClaudeContentBlock.TextBlock("done")] };

        var completed = await host.ResolveAsync("reject", ApprovalDecision.Reject);
        var completedUi = AgentHistoryMapper.ProjectForUi(completed, adapter.GetReasoningSnapshot());
        Assert.AreEqual("final reasoning", completedUi.Last().ReasoningContent);
        Assert.AreEqual("done", completedUi.Last().Content);
        Assert.AreEqual(3, completedUi.Count(message => message.Role == "tool"));

        Assert.AreEqual(2, client.Calls);
        Assert.AreEqual(1, writes);
        Assert.AreEqual("original reasoning", client.Messages[1].ReasoningContent);
        var results = client.Messages.Skip(2).Select(message => Blocks(message).Single()).ToArray();
        CollectionAssert.AreEqual(new[] { "read", "approve", "reject" }, results.Select(result => result.ToolUseId).ToArray());
        Assert.AreEqual("document", results[0].Content);
        Assert.AreEqual("updated", results[1].Content);
        Assert.AreEqual("REJECTED: User rejected this change. Do NOT retry the same edit. Ask the user what they want instead.", results[2].Content);
        var rejected = completed.Results.Single(result => result.CallId == "reject");
        Assert.AreEqual(ToolOutcome.Rejected, rejected.Outcome);
        Assert.AreEqual("Tool call rejected.", rejected.Error);
        Assert.IsNull(rejected.Output);
    }

    [TestMethod]
    public async Task ResultText_UsesOutcomeNotErrorStringAndDoesNotMutateEvidence()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = CreateClient(services);
        var results = new[]
        {
            new ToolResult("failed", "Read", ToolOutcome.Failed, Error: "Tool call rejected."),
            new ToolResult("success", "Read", ToolOutcome.Succeeded, new JValue("Tool call rejected.")),
            new ToolResult("rejected", "Write", ToolOutcome.Rejected, new JValue("ignored output"))
        };
        var request = new AgentModelRequest([TranscriptMessage.Tool(results)], []);
        var before = JToken.FromObject(request.Transcript);

        await new ClaudeAgentModelClient(client, []).CompleteAsync(request, default);

        Assert.AreEqual("Tool call rejected.", Blocks(client.Messages[0]).Single().Content);
        Assert.AreEqual("Tool call rejected.", Blocks(client.Messages[1]).Single().Content);
        Assert.AreEqual(AgentToolResultText.Rejected, Blocks(client.Messages[2]).Single().Content);
        Assert.IsTrue(JToken.DeepEquals(before, JToken.FromObject(request.Transcript)));
    }

    [TestMethod]
    public async Task ReasoningSnapshot_IsReadOnlyIndependentAndUsesAbsoluteCanonicalIndices()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var client = CreateClient(services);
        var initial = new Dictionary<int, string?> { [2] = "imported" };
        var adapter = new ClaudeAgentModelClient(client, [], initialReasoning: initial);
        initial[2] = "changed source";
        var before = adapter.GetReasoningSnapshot();
        Assert.AreEqual("imported", before[2]);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IDictionary<int, string?>)before)[2] = "changed snapshot");
        client.Response = new ClaudeResponse { ReasoningContent = "new thought", Content = [ClaudeContentBlock.TextBlock("new answer")] };
        var transcript = new[] { TranscriptMessage.System("system"), TranscriptMessage.User("go"), TranscriptMessage.Assistant([new TextBlock("old")]) };
        var response = await adapter.CompleteAsync(new AgentModelRequest(transcript, []), default);
        var after = adapter.GetReasoningSnapshot();
        Assert.IsFalse(before.ContainsKey(3));
        Assert.AreEqual("new thought", after[3]);
        var ui = AgentHistoryMapper.ProjectForUi(transcript.Append(TranscriptMessage.Assistant(response.Content)).ToArray(), after);
        Assert.AreEqual("imported", ui[2].ReasoningContent);
        Assert.AreEqual("new thought", ui[3].ReasoningContent);
    }

    private static List<ClaudeContentBlock> Blocks(ClaudeMessage message) => (List<ClaudeContentBlock>)message.Content;
    private static CapturingClient CreateClient(ServiceProvider services) => new(services.GetRequiredService<IServiceScopeFactory>());

    private sealed class CapturingClient(IServiceScopeFactory scopes) : ClaudeClient(scopes, NullLogger<ClaudeClient>.Instance)
    {
        public ClaudeResponse Response { get; set; } = new();
        public Exception? Error { get; set; }
        public string? System { get; private set; }
        public List<ClaudeMessage> Messages { get; private set; } = [];
        public List<ClaudeTool>? Tools { get; private set; }
        public CancellationToken Token { get; private set; }
        public int MaxTokens { get; private set; }
        public int Calls { get; private set; }

        public override Task<ClaudeResponse> SendAsync(string systemPrompt, List<ClaudeMessage> messages, List<ClaudeTool>? tools, CancellationToken ct = default, int maxTokens = 4096)
        {
            Calls++;
            System = systemPrompt;
            Messages = messages;
            Tools = tools;
            Token = ct;
            MaxTokens = maxTokens;
            return Error == null ? Task.FromResult(Response) : Task.FromException<ClaudeResponse>(Error);
        }
    }
}
