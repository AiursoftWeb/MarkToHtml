using Aiursoft.MarkToHtml.Services.Agent;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class AgentToolAdapterTests
{
    [TestMethod]
    public void Definition_PreservesOriginalMcpDescriptionSchemaAndAdviceMetadata()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var registry = new ToolRegistry(services);
        foreach (var tool in registry.AllTools)
        {
            var adapter = registry.IsWriteTool(tool.ProtocolTool.Name)
                ? AgentToolAdapter.ForApprovedWrite(tool, NoOp)
                : AgentToolAdapter.ForReadOnly(tool, NoOp);
            var definition = adapter.Definition;
            Assert.AreEqual(tool.ProtocolTool.Name, definition.Name);
            Assert.AreEqual(tool.ProtocolTool.Description, definition.Description);
            Assert.IsTrue(JToken.DeepEquals(JToken.Parse(tool.ProtocolTool.InputSchema.GetRawText()), definition.InputSchema));
            Assert.AreEqual(registry.IsWriteTool(tool.ProtocolTool.Name), definition.RequiresApproval);

            ((JObject)definition.InputSchema)["changed"] = true;
            Assert.IsNull(adapter.Definition.InputSchema["changed"]);
        }
    }

    [TestMethod]
    public void Factories_RejectReadWriteHandlerMixupsWithoutExecution()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var registry = new ToolRegistry(services);
        Assert.ThrowsExactly<ArgumentException>(() => AgentToolAdapter.ForReadOnly(registry.GetTool("ReplaceText")!, NoOp));
        Assert.ThrowsExactly<ArgumentException>(() => AgentToolAdapter.ForApprovedWrite(registry.GetTool("ReadFullDocument")!, NoOp));
    }

    [TestMethod]
    public async Task Execution_IsolatesArgumentsAndPreservesRawValuesAndText()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var arguments = JObject.Parse("{\"empty\":\"\",\"nil\":null,\"nested\":{\"items\":[1,true,\"text\"]}}");
        var original = arguments.DeepClone();
        using var cancellation = new CancellationTokenSource();
        var expectedToken = cancellation.Token;
        var adapter = AgentToolAdapter.ForReadOnly(ReadTool(services), (received, token) =>
        {
            Assert.AreEqual(expectedToken, token);
            Assert.IsTrue(JToken.DeepEquals(original, received));
            received["nested"]!["items"]![0] = 42;
            received["empty"] = "changed";
            return ValueTask.FromResult("Error: old_string not found.\n\"quoted\"");
        });

        var result = await adapter.ExecuteAsync(arguments, cancellation.Token);

        Assert.IsTrue(JToken.DeepEquals(original, arguments));
        Assert.AreEqual(JTokenType.String, result.Type);
        Assert.AreEqual("Error: old_string not found.\n\"quoted\"", result.Value<string>());
    }

    [TestMethod]
    public async Task Execution_DoesNotConvertExceptionsToLegacyErrorText()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var failure = new InvalidOperationException("original error");
        var adapter = AgentToolAdapter.ForReadOnly(ReadTool(services), (_, _) => throw failure);

        var actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await adapter.ExecuteAsync(new JObject(), default));

        Assert.AreSame(failure, actual);
        // A future host, not this adapter, retains AgentService's "Error executing tool: ..." policy.
    }

    [TestMethod]
    public async Task Execution_CancelledBeforeDispatchDoesNotInvokeHandler()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var called = false;
        var adapter = AgentToolAdapter.ForReadOnly(ReadTool(services), (_, _) =>
        {
            called = true;
            return ValueTask.FromResult("unused");
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await adapter.ExecuteAsync(new JObject(), cancellation.Token));
        Assert.IsFalse(called);
    }

    [TestMethod]
    public async Task ApprovalMetadata_IsNotAnAuthorizationGate_HostOwnsDispatch()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var called = false;
        var adapter = AgentToolAdapter.ForApprovedWrite(new ToolRegistry(services).GetTool("ReplaceText")!, (_, _) =>
        {
            called = true;
            return ValueTask.FromResult("host's approved result");
        });

        Assert.IsTrue(adapter.Definition.RequiresApproval);
        Assert.IsFalse(called);
        var result = await adapter.ExecuteAsync(new JObject(), default);
        Assert.IsTrue(called);
        Assert.AreEqual("host's approved result", result.Value<string>());
        // Deliberately a fake handler: no MCP invocation, persistence, or approval semantics here.
    }

    private static McpServerTool ReadTool(IServiceProvider services) =>
        new ToolRegistry(services).GetTool("ReadFullDocument")!;

    private static ValueTask<string> NoOp(JToken arguments, CancellationToken cancellationToken) =>
        ValueTask.FromResult("unused");
}
