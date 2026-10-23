using Aiursoft.AgentKit;
using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Messages;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.InMemory;
using Aiursoft.MarkToHtml.Services.Agent;
using Aiursoft.MarkToHtml.Services.Tools;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class AgentSessionToolFactoryTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RealBindings_PausePersistAndResumeOnlyAfterLastDecision(bool rejectLast)
    {
        var conversation = new AgentConversation
        {
            UserId = "trusted-owner", DocumentId = Guid.NewGuid(), DocumentContent = "Original document"
        };
        var observedScopes = new List<CurrentUserService>();
        var services = new ServiceCollection();
        services.AddScoped<CurrentUserService>();
        services.AddScoped(provider =>
        {
            var current = provider.GetRequiredService<CurrentUserService>();
            // Actual MCP target construction observes the helper's trusted context,
            // not UserId/DocumentId/DocumentContent supplied as model arguments.
            Assert.AreEqual(conversation.UserId, current.UserId);
            Assert.AreEqual(conversation.DocumentId, current.DocumentId);
            Assert.AreEqual(conversation.DocumentContent, current.DocumentContent);
            observedScopes.Add(current);
            return new DocumentReadTools(current);
        });
        var database = Guid.NewGuid().ToString();
        services.AddDbContext<InMemoryContext>(options => options.UseInMemoryDatabase(database));
        services.AddScoped<TemplateDbContext>(provider => provider.GetRequiredService<InMemoryContext>());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var previousUpdate = DateTime.UtcNow.AddDays(-1);
        using (var seed = provider.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = conversation.DocumentId, UserId = conversation.UserId,
                Content = conversation.DocumentContent, UpdatedAt = previousUpdate
            });
            await db.SaveChangesAsync();
        }

        var registry = new ToolRegistry(provider);
        IReadOnlyList<IAgentTool> tools;
        // A request scope may end before a long session starts; only its factory survives.
        using (var requestScope = provider.CreateScope())
            tools = AgentSessionToolFactory.Create(requestScope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(), conversation, registry);
        foreach (var tool in tools)
        {
            var original = registry.GetTool(tool.Definition.Name)!.ProtocolTool;
            Assert.AreEqual(registry.IsWriteTool(original.Name), tool.Definition.RequiresApproval);
            Assert.IsTrue(JToken.DeepEquals(JToken.Parse(original.InputSchema.GetRawText()), tool.Definition.InputSchema));
        }
        var firstWrite = Replacement("w1", "Original", "Edited");
        var secondWrite = Replacement("w2", "document", "text");
        var originalArguments = firstWrite.Call.Arguments.DeepClone();
        var finalContent = rejectLast ? "Edited document" : "Edited text";
        IServiceProvider verificationServices = provider;
        var model = new ScriptedModel((index, request) =>
        {
            if (index == 0) return Calls(Read("r1"), firstWrite, Read("r2"), secondWrite);
            if (index == 1)
            {
                Assert.AreEqual(finalContent, conversation.DocumentContent);
                Assert.AreEqual(finalContent, Stored(verificationServices).Content, "Persistence must precede model resume.");
                var batch = request.Transcript.Single(message => message.Role == TranscriptRole.Tool);
                CollectionAssert.AreEqual(new[] { "r1", "w1", "r2", "w2" }, batch.ToolResults.Select(result => result.CallId).ToArray());
                Assert.AreEqual("1|Original document", batch.ToolResults[0].Output!.Value<string>());
                Assert.AreEqual("1|Original document", batch.ToolResults[2].Output!.Value<string>());
                if (rejectLast) Assert.AreEqual(ToolOutcome.Rejected, batch.ToolResults[3].Outcome);
                return Calls(Read("verify"));
            }
            Assert.AreEqual(2, index);
            Assert.AreEqual($"1|{finalContent}", request.Transcript.Last().ToolResults.Single().Output!.Value<string>());
            return new AgentModelResponse([new TextBlock("verified")]);
        });
        var host = new OrderedAgentSessionHost([TranscriptMessage.User("edit")], tools, model);

        var paused = await host.StartAsync();
        Assert.AreEqual(OrderedAgentRunOutcome.AwaitingApproval, paused.Outcome);
        Assert.AreEqual(1, model.Requests);
        Assert.AreEqual("Original document", conversation.DocumentContent);
        Assert.AreEqual("Original document", Stored(provider).Content);
        Assert.AreEqual(previousUpdate, Stored(provider).UpdatedAt);
        Assert.IsFalse(conversation.DocumentContentUpdated);
        Assert.AreEqual(2, observedScopes.Count);
        CollectionAssert.AreEqual(new[] { "1|Original document", "1|Original document" },
            paused.Checkpoint!.CompletedResults.Select(result => result.Output!.Value<string>()).ToArray());
        // Neither returned checkpoints nor the model's own argument objects can alter approval input.
        paused.Checkpoint.Resolutions.Single(item => item.Call.Id == "w1").Call.Arguments["newString"] = "tampered checkpoint";
        firstWrite.Call.Arguments["newString"] = "tampered model";

        var partial = await host.ResolveAsync("w1", ApprovalDecision.Execute);
        Assert.AreEqual(OrderedAgentRunOutcome.AwaitingApproval, partial.Outcome);
        Assert.AreEqual(1, model.Requests);
        Assert.AreEqual("Edited document", conversation.DocumentContent);
        Assert.AreEqual("Edited document", Stored(provider).Content);
        Assert.IsTrue(conversation.DocumentContentUpdated);
        Assert.IsTrue(Stored(provider).UpdatedAt > previousUpdate);
        var approvedArguments = partial.Checkpoint!.Resolutions.Single(item => item.Call.Id == "w1").Call.Arguments;
        Assert.IsTrue(JToken.DeepEquals(originalArguments, approvedArguments));
        var afterFirstWrite = Stored(provider).UpdatedAt;

        var completed = await host.ResolveAsync("w2", rejectLast ? ApprovalDecision.Reject : ApprovalDecision.Execute);
        Assert.AreEqual(OrderedAgentRunOutcome.Completed, completed.Outcome);
        Assert.AreEqual("verified", completed.FinalText);
        Assert.AreEqual(3, model.Requests);
        Assert.AreEqual(finalContent, Stored(provider).Content);
        Assert.AreEqual(3, observedScopes.Count);
        Assert.AreEqual(3, observedScopes.Distinct().Count());
        if (rejectLast)
        {
            Assert.AreEqual(afterFirstWrite, Stored(provider).UpdatedAt);
            // Known production blocker: generic runner rejection text differs from AgentService.
            Assert.AreEqual("Tool call rejected.", completed.Results.Single(result => result.CallId == "w2").Error);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HandlerExceptions_PreserveLegacyText_NotBusinessSuccess(bool write)
    {
        // Deliberately missing CurrentUserService/DB. No network, host workers or queue.
        using var provider = new ServiceCollection().BuildServiceProvider();
        var conversation = new AgentConversation { DocumentContent = "Original document" };
        var registry = new ToolRegistry(provider);
        var model = new ScriptedModel((index, _) => index == 0
            ? Calls(write ? Replacement("call", "Original", "Edited") : Read("call"))
            : new AgentModelResponse([new TextBlock("continued")]));
        var host = new OrderedAgentSessionHost([TranscriptMessage.User("test")],
            AgentSessionToolFactory.Create(provider.GetRequiredService<IServiceScopeFactory>(), conversation, registry), model);
        var result = await host.StartAsync();
        if (write) result = await host.ResolveAsync("call", ApprovalDecision.Execute);

        var output = result.Results.Single();
        Assert.AreEqual(JTokenType.String, output.Output!.Type);
        StringAssert.StartsWith(output.Output.Value<string>()!, write ? "Error executing tool: " : "Error executing ReadFullDocument: ");
        // Compatibility strings are returned dispatches, not a reliable evaluator success signal.
        Assert.AreEqual(ToolOutcome.Succeeded, output.Outcome);
        Assert.AreEqual(write ? "Edited document" : "Original document", conversation.DocumentContent);
        Assert.IsFalse(conversation.DocumentContentUpdated);
        Assert.AreEqual(2, model.Requests);
    }

    private static MarkdownDocument Stored(IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<TemplateDbContext>().MarkdownDocuments.AsNoTracking().Single();
    }

    private static ToolCallBlock Read(string id) => new(new ToolCall(id, "ReadFullDocument", new JObject
    {
        ["UserId"] = "model-attacker", ["DocumentId"] = Guid.NewGuid().ToString(), ["DocumentContent"] = "poisoned"
    }));
    private static ToolCallBlock Replacement(string id, string oldText, string newText) => new(new ToolCall(id, "ReplaceText", new JObject
    {
        ["oldString"] = oldText, ["newString"] = newText, ["UserId"] = "model-attacker"
    }));
    private static AgentModelResponse Calls(params ToolCallBlock[] calls) => new(calls, AgentFinishReason.ToolCalls);

    private sealed class ScriptedModel(Func<int, AgentModelRequest, AgentModelResponse> respond) : IAgentModelClient
    {
        public int Requests { get; private set; }
        public ValueTask<AgentModelResponse> CompleteAsync(AgentModelRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(respond(Requests++, request));
        }
    }
}
