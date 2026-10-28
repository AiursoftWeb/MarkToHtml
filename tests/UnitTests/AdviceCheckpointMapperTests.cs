using Aiursoft.AgentKit;
using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Messages;
using Aiursoft.MarkToHtml.Services.Agent;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class AdviceCheckpointMapperTests
{
    [TestMethod]
    public async Task MixedBatch_StableAdvice_PartialExecution_FinalRejection_AndReusedId()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var registry = new ToolRegistry(services);
        var service = new AdviceService();
        var conversation = new AgentConversation { DocumentContent = "original document" };
        var mapper = new AdviceCheckpointMapper(conversation.Id, service, registry);
        var executed = new List<string>();
        var read = AgentToolAdapter.ForReadOnly(registry.GetTool("ReadFullDocument")!, (_, _) => ValueTask.FromResult("read"));
        var write = AgentToolAdapter.ForApprovedWrite(registry.GetTool("ReplaceText")!, (args, _) =>
        {
            executed.Add(args["newString"]!.Value<string>()!);
            conversation.DocumentContent = "edited document";
            return ValueTask.FromResult("Error: business result, not a runner failure");
        });
        var model = new ScriptedModel(Calls(Call("r", "ReadFullDocument"), Replace("a"), Replace("b")),
            Calls(Replace("a")), new AgentModelResponse([new TextBlock("done")]));
        var host = new OrderedAgentSessionHost([TranscriptMessage.User("edit")], [read, write], model);
        var paused = await host.StartAsync();
        mapper.Synchronize(conversation, paused);
        var ids = conversation.PendingAdviceIds.ToArray();
        Assert.AreEqual(2, ids.Length);
        var first = service.Get(ids[0])!;
        var second = service.Get(ids[1])!;
        Assert.AreEqual("a", first.ToolCallId);
        Assert.AreEqual("original document", first.DocumentContentSnapshot);
        Assert.AreEqual("original document", second.DocumentContentSnapshot);
        Assert.AreEqual(registry.GetTool("ReplaceText")!.ProtocolTool.Title ?? "ReplaceText", first.ToolDisplayName);
        Assert.AreEqual(registry.GetTool("ReplaceText")!.ProtocolTool.Description, first.ToolDescription);
        Assert.AreEqual("Edit Document (Replace: original, With: edited)", first.ParameterDisplay);
        Assert.AreEqual("original", first.Parameters["oldString"]);
        CollectionAssert.AreEqual(new[] { "-original", "+edited" }, first.DiffHunks!.Single().Lines);
        mapper.Synchronize(conversation, paused);
        CollectionAssert.AreEqual(ids, conversation.PendingAdviceIds.ToArray());

        // Legacy approval removes PendingIds before queued dispatch; syncing cannot recreate/reset it.
        service.UpdateStatus(first.Id, AdviceStatus.Approved);
        conversation.PendingAdviceIds.Remove(first.Id);
        mapper.Synchronize(conversation, paused);
        Assert.AreEqual(AdviceStatus.Approved, first.Status);
        CollectionAssert.AreEqual(new[] { second.Id }, conversation.PendingAdviceIds.ToArray());
        var partial = await host.ResolveAsync("a", ApprovalDecision.Execute);
        mapper.Synchronize(conversation, partial);
        Assert.AreEqual(AdviceStatus.Executed, first.Status);
        Assert.AreEqual("Error: business result, not a runner failure", first.Result);
        Assert.IsNull(first.Error);
        Assert.AreEqual(AdviceStatus.Pending, second.Status);
        Assert.AreEqual("original document", second.DocumentContentSnapshot);
        Assert.AreEqual(1, model.Requests);
        mapper.Synchronize(conversation, partial);

        // Last rejection resumes the model into a new batch which reuses the old call ID.
        var next = await host.ResolveAsync("b", ApprovalDecision.Reject);
        mapper.Synchronize(conversation, next);
        Assert.AreEqual(AdviceStatus.Rejected, second.Status);
        Assert.AreEqual(AgentToolResultMapper.Rejected, second.Result);
        Assert.IsNull(second.Error);
        var third = service.Get(conversation.PendingAdviceIds.Single())!;
        Assert.AreNotEqual(first.Id, third.Id);
        Assert.AreEqual("a", third.ToolCallId);
        Assert.AreEqual("edited document", third.DocumentContentSnapshot);
        Assert.AreEqual(AdviceStatus.Executed, first.Status);
        Assert.AreEqual(1, executed.Count);
        mapper.Synchronize(conversation, next);
        Assert.AreEqual(third.Id, conversation.PendingAdviceIds.Single());
        var final = await host.ResolveAsync("a", ApprovalDecision.Reject);
        mapper.Synchronize(conversation, final);
        mapper.Synchronize(conversation, final);
        Assert.AreEqual(AdviceStatus.Rejected, third.Status);
        Assert.AreEqual(0, conversation.PendingAdviceIds.Count);
        Assert.AreEqual(0, service.GetPendingForConversation(conversation.Id).Count);
        Assert.AreEqual(1, executed.Count);
        Assert.AreEqual(3, model.Requests);
    }

    [TestMethod]
    public async Task InvalidAssociations_ValidateBeforeAnyMutation_AndFailedOutcomeIsExplicit()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var registry = new ToolRegistry(services);
        var service = new AdviceService();
        var conversation = new AgentConversation();
        var mapper = new AdviceCheckpointMapper(conversation.Id, service, registry);
        var write = AgentToolAdapter.ForApprovedWrite(registry.GetTool("ReplaceText")!, (_, _) => throw new InvalidOperationException("private"));
        var host = new OrderedAgentSessionHost([TranscriptMessage.User("edit")], [write],
            new ScriptedModel(Calls(Replace("a"), Replace("b")), new AgentModelResponse([new TextBlock("done")])));
        var paused = await host.StartAsync();
        mapper.Synchronize(conversation, paused);
        var ids = conversation.PendingAdviceIds.ToArray();
        var other = new AgentConversation();
        Assert.ThrowsExactly<ArgumentException>(() => mapper.Synchronize(other, paused));
        Assert.AreEqual(0, other.PendingAdviceIds.Count);
        var partial = await host.ResolveAsync("a", ApprovalDecision.Execute);
        var mismatched = partial.Checkpoint! with { Transcript = [TranscriptMessage.User("unrelated")] };
        Assert.ThrowsExactly<ArgumentException>(() => mapper.Synchronize(conversation, partial with { Checkpoint = mismatched }));
        Assert.IsTrue(ids.All(id => service.Get(id)!.Status == AdviceStatus.Pending));
        CollectionAssert.AreEqual(ids, conversation.PendingAdviceIds.ToArray());
        var invalid = partial.Checkpoint!.DeepCopy();
        var badResult = invalid.Resolutions[1].Call;
        invalid = invalid with { Resolutions = [invalid.Resolutions[0], new OrderedToolResolution(badResult,
            new ToolResult("wrong-id", badResult.Name, ToolOutcome.Rejected))] };
        Assert.ThrowsExactly<ArgumentException>(() => mapper.Synchronize(conversation, partial with { Checkpoint = invalid }));
        Assert.IsTrue(ids.All(id => service.Get(id)!.Status == AdviceStatus.Pending));
        CollectionAssert.AreEqual(ids, conversation.PendingAdviceIds.ToArray());
        mapper.Synchronize(conversation, partial);
        Assert.AreEqual(AdviceStatus.Failed, service.Get(ids[0])!.Status);
        Assert.AreEqual("Tool execution failed.", service.Get(ids[0])!.Error);
        var final = await host.ResolveAsync("b", ApprovalDecision.Reject);
        var tampered = final with { Results = [final.Results[0] with { CallId = "wrong-id" }, final.Results[1]] };
        Assert.ThrowsExactly<ArgumentException>(() => mapper.Synchronize(conversation, tampered));
        Assert.AreEqual(AdviceStatus.Pending, service.Get(ids[1])!.Status);
        mapper.Synchronize(conversation, final);
        Assert.AreEqual(AdviceStatus.Rejected, service.Get(ids[1])!.Status);
    }

    private static ToolCallBlock Replace(string id) => new(new ToolCall(id, "ReplaceText", new JObject
    {
        ["oldString"] = "original", ["newString"] = "edited"
    }));
    private static ToolCallBlock Call(string id, string name) => new(new ToolCall(id, name, new JObject()));
    private static AgentModelResponse Calls(params ToolCallBlock[] calls) => new(calls, AgentFinishReason.ToolCalls);
    private sealed class ScriptedModel(params AgentModelResponse[] responses) : IAgentModelClient
    {
        public int Requests { get; private set; }
        public ValueTask<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(responses[Requests++]);
    }
}
