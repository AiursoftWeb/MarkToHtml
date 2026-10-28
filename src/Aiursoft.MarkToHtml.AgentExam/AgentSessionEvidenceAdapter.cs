using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Evaluator;
using Aiursoft.AgentKit.Messages;
using Aiursoft.MarkToHtml.Services.Agent;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.AgentExam;

/// <summary>
/// Offline current-turn evidence, built directly rather than using globally unique call IDs.
/// The facade supplies a trusted absolute turn boundary excluding lossy imports, earlier turns
/// and host-authored budget notices. IterationCount counts model attempts (including failures),
/// not appended completions. Tool Iteration is the current-turn assistant completion ordinal.
/// DocumentContent is an in-memory snapshot, NOT proof of persistence or business success.
/// Rejections and unresolved proposals are state only, never executed tool evidence.
/// Timing is unavailable at step level (zero); individual durations come from runner traces.
/// No feedback text, reasoning, exception bodies or mutable UI history is used.
/// </summary>
internal static class AgentSessionEvidenceAdapter
{
    internal static EvaluationStepEvidence ToStepEvidence(int stepIndex, AgentSessionRuntimeSnapshot snapshot)
    {
        var (run, turnStartIndex, documentContent, availableTools) = snapshot;
        if (turnStartIndex < 0 || turnStartIndex > run.Transcript.Count)
            throw new ArgumentOutOfRangeException(nameof(snapshot), "Turn boundary is outside the transcript.");
        var tools = new List<EvaluationToolTrace>();
        var resolutions = new JArray();
        var dispatched = new List<(int Batch, int Iteration, ToolCall Call, ToolResult Result)>();
        var iteration = 0;
        for (var batch = turnStartIndex; batch < run.Transcript.Count; batch++)
        {
            var message = run.Transcript[batch];
            if (message.Role != TranscriptRole.Assistant) continue;
            iteration++;
            foreach (var call in message.ToolCalls)
            {
                // Completed batches have one adjacent tool message. Partial batches retain
                // results only in checkpoint resolutions, not run.Results or transcript.
                var result = batch + 1 < run.Transcript.Count
                    ? run.Transcript[batch + 1].ToolResults.SingleOrDefault(item => item.CallId == call.Id)
                    : run.Checkpoint?.Resolutions.SingleOrDefault(item => item.Call.Id == call.Id)?.Result;
                if (result != null && result.Name != call.Name)
                    throw new InvalidOperationException("Canonical tool correlation is inconsistent.");
                resolutions.Add(new JObject
                {
                    ["BatchIndex"] = batch, ["CallId"] = call.Id, ["Name"] = call.Name,
                    ["Outcome"] = result?.Outcome.ToString() ?? "Unresolved"
                });
                if (result?.Outcome is ToolOutcome.Succeeded or ToolOutcome.Failed)
                    dispatched.Add((batch, iteration, call, result));
            }
        }

        var traces = run.Checkpoint?.CompletedTraces ?? run.Traces;
        // OrderedRunner may lose earlier traces when resolving a reused call ID. Never
        // reconstruct a dispatch duration or claim complete evidence from lossy correlation.
        var correlated = traces.Count == dispatched.Count &&
            dispatched.All(item => availableTools.Contains(item.Call.Name));
        for (var i = 0; correlated && i < traces.Count; i++)
        {
            var trace = traces[i];
            var item = dispatched[i];
            correlated = trace.CallId == item.Call.Id && trace.ToolName == item.Call.Name &&
                trace.Outcome == item.Result.Outcome;
            if (correlated)
                tools.Add(new EvaluationToolTrace(item.Call.Name, item.Call.Arguments.DeepClone(),
                    item.Result.Output?.DeepClone() ?? JValue.CreateNull(), item.Iteration,
                    trace.Duration, $"{item.Batch}:{item.Call.Id}", item.Result.Outcome));
        }
        if (!correlated) tools.Clear();
        var valid = correlated && run.Outcome == OrderedAgentRunOutcome.Completed;
        return new EvaluationStepEvidence(stepIndex, null, null, tools.ToArray(), new JObject
        {
            ["DocumentContent"] = documentContent,
            ["DocumentContentSource"] = "InMemory",
            ["PersistenceVerified"] = false,
            ["Outcome"] = run.Outcome.ToString(),
            ["Resolutions"] = resolutions
        }, run.FinalText, run.Iterations, TimeSpan.Zero, valid,
            !correlated ? "Canonical dispatch traces cannot be correlated without loss." :
            valid ? null : $"Agent turn ended with outcome '{run.Outcome}'.");
    }
}
