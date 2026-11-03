using Aiursoft.AgentKit;
using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Evaluator;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.InMemory;
using Aiursoft.MarkToHtml.Services.Agent;
using Aiursoft.MarkToHtml.Services.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.AgentExam;

public enum ExamScenario { ReadOnly, ApprovedWrite, RejectedWrite, FailedWrite, Continuation }

/// <summary>
/// Deterministic offline contract exam, not a model-quality benchmark. No application host,
/// configuration or credentials are loaded. This fixture owns its seeded document and trusted
/// identity; Execute/Reject below are explicit fixture authorization, not a production policy.
/// </summary>
public sealed class ExamExecutor
{
    private const string Original = "Original document";
    private const string Edited = "Edited document";

    public async Task<EvaluationCaseResult> RunAsync(ExamScenario scenario)
    {
        if (!Enum.IsDefined(scenario)) throw new ArgumentOutOfRangeException(nameof(scenario));
        var conversation = new AgentConversation
        {
            UserId = "offline-exam-owner", DocumentId = Guid.NewGuid(), DocumentContent = Original,
            Messages = [new ToolMessagesItem { Role = "system", Content = "Offline fixture" },
                new ToolMessagesItem { Role = "user", Content = scenario.ToString() }]
        };
        var persistenceDispatches = 0;
        var services = new ServiceCollection();
        services.AddScoped<CurrentUserService>();
        services.AddScoped<DocumentReadTools>();
        var database = Guid.NewGuid().ToString();
        services.AddDbContext<InMemoryContext>(options => options.UseInMemoryDatabase(database));
        services.AddScoped<TemplateDbContext>(sp =>
        {
            persistenceDispatches++;
            if (scenario == ExamScenario.FailedWrite)
                throw new InvalidOperationException("private offline persistence detail");
            return sp.GetRequiredService<InMemoryContext>();
        });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using (var seed = provider.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<InMemoryContext>();
            db.MarkdownDocuments.Add(new MarkdownDocument
            {
                Id = conversation.DocumentId, UserId = conversation.UserId, Content = Original
            });
            await db.SaveChangesAsync();
        }
        var scopes = provider.GetRequiredService<IServiceScopeFactory>();
        var client = new ScriptedExamModel(scopes, scenario);
        var session = new AgentSessionFacade(conversation, client, scopes, new ToolRegistry(provider), new AdviceService());
        var initial = await session.StartAsync();
        var read = scenario is ExamScenario.ReadOnly or ExamScenario.Continuation;
        var pausedCorrectly = read
            ? initial.Outcome == OrderedAgentRunOutcome.Completed
            : initial.Outcome == OrderedAgentRunOutcome.AwaitingApproval &&
              client.InvocationCount == 1 && persistenceDispatches == 0 && conversation.DocumentContent == Original;
        if (!read)
            await session.ResolveAsync(conversation.PendingAdviceIds.Single(), scenario == ExamScenario.RejectedWrite
                ? ApprovalDecision.Reject : ApprovalDecision.Execute);
        var previousTurnEnd = (await session.GetRuntimeSnapshotAsync()).Run.Transcript.Count;
        if (scenario == ExamScenario.Continuation)
            await session.ContinueAsync(conversation.UserId, "Read the latest editor snapshot.", Edited);

        var evidence = AgentSessionEvidenceAdapter.ToStepEvidence(0, await session.GetRuntimeSnapshotAsync());
        // A fresh context, queried without tracking, is proof of fixture persistence. Keep the
        // adapter's DocumentContent/InMemory/PersistenceVerified=false metadata unchanged.
        using (var verify = provider.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<InMemoryContext>();
            var stored = await db.MarkdownDocuments.AsNoTracking().SingleAsync(document =>
                document.Id == conversation.DocumentId && document.UserId == conversation.UserId);
            evidence.State["PersistedDocumentContent"] = stored.Content;
        }
        evidence.State["PersistedDocumentSource"] = "FreshInMemoryDbContext";
        evidence.State["ModelInvocations"] = client.InvocationCount;
        evidence.State["PersistenceDispatches"] = persistenceDispatches;
        evidence.State["ApprovalBoundaryVerified"] = pausedCorrectly;
        if (scenario == ExamScenario.FailedWrite)
            evidence.State["SafeRecoveryFeedbackVerified"] = client.SafeRecoveryFeedbackVerified;
        if (scenario == ExamScenario.Continuation)
            evidence.State["CurrentTurnBoundaryVerified"] = evidence.Tools.Count == 1 &&
                evidence.Tools.Single().CallId == $"{previousTurnEnd + 3}:fixture-call";
        return new AssertionEvaluator().Evaluate(CreateCase(scenario), new EvaluationEvidence([evidence]));
    }

    internal static EvaluationCase CreateCase(ExamScenario scenario)
    {
        var approved = scenario == ExamScenario.ApprovedWrite;
        var read = scenario is ExamScenario.ReadOnly or ExamScenario.Continuation;
        var failed = scenario == ExamScenario.FailedWrite;
        static EvaluationAssertion Check(string id, string kind, JToken match) =>
            new(id, kind, "correctness", 1, 0, true, false, match);
        return new EvaluationCase(scenario.ToString(), 1,
        [
            new EvaluationStep(0, null, null, new EvaluationExpectation(
            [
                Check("persisted-document", EvaluationAssertionKinds.State,
                    new JObject { ["PersistedDocumentContent"] = approved ? Edited : Original,
                        ["PersistedDocumentSource"] = "FreshInMemoryDbContext" }),
                Check("execution-boundaries", EvaluationAssertionKinds.State,
                    new JObject { ["Outcome"] = "Completed", ["ModelInvocations"] = scenario == ExamScenario.Continuation ? 4 : 2,
                        ["PersistenceDispatches"] = approved || failed ? 1 : 0, ["ApprovalBoundaryVerified"] = true }),
                Check("resolution", EvaluationAssertionKinds.State, new JObject
                {
                    ["Resolutions"] = new JArray(new JObject { ["CallId"] = "fixture-call",
                        ["Name"] = read ? "ReadFullDocument" : "ReplaceText",
                        ["Outcome"] = scenario == ExamScenario.RejectedWrite ? "Rejected" : failed ? "Failed" : "Succeeded" })
                }),
                scenario == ExamScenario.RejectedWrite
                    ? new EvaluationAssertion("dispatch", EvaluationAssertionKinds.ForbidTool, "correctness",
                        0, -1, true, true, new JObject { ["name"] = "ReplaceText" })
                    : Check("dispatch", EvaluationAssertionKinds.Tool, new JObject
                    {
                        ["name"] = read ? "ReadFullDocument" : "ReplaceText",
                        ["minCount"] = 1, ["maxCount"] = 1,
                        ["parameters"] = read ? new JObject() : new JObject
                            { ["oldString"] = "Original", ["newString"] = "Edited" }
                    }),
                Check("tool-budget", EvaluationAssertionKinds.MaxToolCalls, new JValue(approved || failed || read ? 1 : 0)),
                .. failed ? new[]
                {
                    Check("safe-recovery", EvaluationAssertionKinds.State,
                        new JObject { ["SafeRecoveryFeedbackVerified"] = true, ["DocumentContent"] = Edited,
                            ["PersistenceVerified"] = false })
                } : Array.Empty<EvaluationAssertion>(),
                .. scenario == ExamScenario.Continuation ? new[]
                {
                    Check("turn-boundary", EvaluationAssertionKinds.State,
                        new JObject { ["CurrentTurnBoundaryVerified"] = true, ["DocumentContent"] = Edited }),
                    Check("turn-budget", EvaluationAssertionKinds.MaxIterations, new JValue(2))
                } : Array.Empty<EvaluationAssertion>()
            ]))
        ]);
    }

    private sealed class ScriptedExamModel(IServiceScopeFactory scopes, ExamScenario scenario)
        : ClaudeClient(scopes, NullLogger<ClaudeClient>.Instance)
    {
        public int InvocationCount { get; private set; }
        public bool SafeRecoveryFeedbackVerified { get; private set; }
        public override Task<ClaudeResponse> SendAsync(string systemPrompt, List<ClaudeMessage> messages,
            List<ClaudeTool>? tools, CancellationToken ct = default, int maxTokens = 4096)
        {
            ct.ThrowIfCancellationRequested();
            InvocationCount++;
            if (scenario == ExamScenario.FailedWrite && InvocationCount == 2)
            {
                var feedback = messages.SelectMany(message => message.Content as List<ClaudeContentBlock> ?? [])
                    .Single(block => block.ToolUseId == "fixture-call").Content?.ToString() ?? "";
                SafeRecoveryFeedbackVerified = feedback.Contains("DOCUMENT_PERSISTENCE_FAILED") &&
                    feedback.Contains("Do not blindly repeat") && feedback.Contains("not proof of persistence") &&
                    !feedback.Contains("private offline persistence detail");
            }
            return Task.FromResult(InvocationCount switch
            {
                1 or 3 when InvocationCount == 1 || scenario == ExamScenario.Continuation => new ClaudeResponse { Content = [scenario is ExamScenario.ReadOnly or ExamScenario.Continuation
                    ? ClaudeContentBlock.ToolUse("fixture-call", "ReadFullDocument", new())
                    : ClaudeContentBlock.ToolUse("fixture-call", "ReplaceText",
                        new() { ["oldString"] = "Original", ["newString"] = "Edited" })] },
                2 or 4 when InvocationCount == 2 || scenario == ExamScenario.Continuation => new ClaudeResponse { Content = [ClaudeContentBlock.TextBlock("Offline fixture complete.")] },
                _ => throw new InvalidOperationException("Offline model script exhausted.")
            });
        }
    }
}
