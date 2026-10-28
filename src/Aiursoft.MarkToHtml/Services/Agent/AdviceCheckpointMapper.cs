using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Messages;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.Services.Agent;

/// <summary>
/// Opt-in projection only: one instance per conversation/host lifetime, with an append-only
/// canonical transcript. Call synchronously under the caller's session serialization (including
/// approval/status changes). No dispatch, authorization, DI registration or production wiring.
/// Absolute assistant index plus call ID identifies a batch, even when later turns reuse IDs.
/// Must observe the initial pause: resolved writes never create retroactive proposals/snapshots.
/// </summary>
internal sealed class AdviceCheckpointMapper(Guid conversationId, AdviceService adviceService, ToolRegistry registry,
    AgentToolResultMapper? feedback = null)
{
    private readonly Dictionary<(int Batch, string CallId), (Guid AdviceId, ToolCall Call)> _links = new();

    internal void Synchronize(AgentConversation conversation, OrderedAgentRunResult run)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(run);
        if (conversation.Id != conversationId)
            throw new ArgumentException("The mapper belongs to another conversation.", nameof(conversation));

        // Validate the entire projection before touching Advice, PendingAdviceIds, or associations.
        var calls = new Dictionary<(int Batch, string CallId), ToolCall>();
        var results = new Dictionary<(int Batch, string CallId), ToolResult>();
        var completed = new List<ToolResult>();
        var pendingBatch = -1;
        for (var index = 0; index < run.Transcript.Count; index++)
        {
            var message = run.Transcript[index];
            if (message.ToolCalls.Count > 0)
            {
                if (message.Role != TranscriptRole.Assistant || pendingBatch >= 0)
                    throw new ArgumentException("Invalid tool batch.", nameof(run));
                pendingBatch = index;
                foreach (var call in message.ToolCalls)
                    if (!calls.TryAdd((index, call.Id), call))
                        throw new ArgumentException("Duplicate call ID in batch.", nameof(run));
            }
            if (message.ToolResults.Count == 0) continue;
            if (message.Role != TranscriptRole.Tool || pendingBatch < 0)
                throw new ArgumentException("Tool result has no batch.", nameof(run));
            foreach (var result in message.ToolResults)
            {
                AddResult(pendingBatch, result);
                completed.Add(result);
            }
            if (calls.Keys.Count(key => key.Batch == pendingBatch) != message.ToolResults.Count)
                throw new ArgumentException("Incomplete completed batch.", nameof(run));
            pendingBatch = -1;
        }

        var unresolved = new List<(int Batch, string CallId)>();
        if (run.Checkpoint is { } checkpoint)
        {
            if (!JToken.DeepEquals(JToken.FromObject(checkpoint.Transcript), JToken.FromObject(run.Transcript)) ||
                pendingBatch != run.Transcript.Count - 1 || pendingBatch < 0 ||
                checkpoint.Resolutions.Count != calls.Keys.Count(key => key.Batch == pendingBatch))
                throw new ArgumentException("Checkpoint does not match the pending batch.", nameof(run));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var resolution in checkpoint.Resolutions)
            {
                var key = (pendingBatch, resolution.Call.Id);
                if (!seen.Add(resolution.Call.Id) || !calls.TryGetValue(key, out var call) || !SameCall(call, resolution.Call))
                    throw new ArgumentException("Checkpoint call does not match its batch.", nameof(run));
                if (resolution.Result is { } result) AddResult(pendingBatch, result);
                else
                {
                    if (!registry.IsWriteTool(call.Name))
                        throw new ArgumentException("Only write proposals may be unresolved.", nameof(run));
                    unresolved.Add(key);
                }
            }
        }
        else if (pendingBatch >= 0)
            throw new ArgumentException("Unresolved batch requires a checkpoint.", nameof(run));

        // Results has no batch indices and the runner may coalesce reused IDs. Validate it
        // against canonical history, but project indexed tool messages, never match Advice by ID alone.
        var remaining = completed.ToList();
        foreach (var result in run.Results)
        {
            var match = remaining.FindIndex(item => SameResult(item, result));
            if (match < 0)
                throw new ArgumentException("Completed results do not match canonical history.", nameof(run));
            remaining.RemoveAt(match);
        }
        foreach (var (key, link) in _links)
        {
            var advice = adviceService.Get(link.AdviceId);
            if (!calls.TryGetValue(key, out var call) || !SameCall(call, link.Call) ||
                advice == null || advice.ConversationId != conversationId ||
                advice.ToolCallId != call.Id || advice.ToolName != call.Name)
                throw new ArgumentException("Advice association does not match this session.", nameof(run));
        }

        foreach (var key in unresolved)
        {
            if (_links.ContainsKey(key)) continue; // Includes Approved proposals absent from GetPending.
            var call = calls[key];
            var args = call.Arguments.ToObject<Dictionary<string, object?>>() ?? new();
            var advice = AgentService.CreateToolAdvice(adviceService, registry, conversation, call.Name, call.Id, args);
            _links.Add(key, (advice.Id, call.DeepCopy()));
        }
        foreach (var (key, link) in _links)
        {
            var advice = adviceService.Get(link.AdviceId)!;
            if (results.TryGetValue(key, out var result) &&
                (advice.Status is AdviceStatus.Pending or AdviceStatus.Approved ||
                 advice.Status == AdviceStatus.Rejected && result.Outcome == ToolOutcome.Rejected))
            {
                if (result.Outcome == ToolOutcome.Rejected)
                {
                    advice.Status = AdviceStatus.Rejected;
                    advice.Result = AgentToolResultMapper.Rejected;
                    advice.Error = null;
                }
                else if (result.Outcome == ToolOutcome.Succeeded)
                    adviceService.SetResult(advice.Id, AgentToolResultMapper.GetText(result), null);
                else if (result.Outcome == ToolOutcome.Failed)
                {
                    var text = feedback?.GetText(key.Batch, result) ?? AgentToolResultMapper.GetText(result);
                    adviceService.SetResult(advice.Id, text,
                        feedback == null ? result.Error ?? "Tool execution failed." : text);
                }
            }
            if (advice.Status == AdviceStatus.Pending)
            {
                if (!conversation.PendingAdviceIds.Contains(advice.Id)) conversation.PendingAdviceIds.Add(advice.Id);
            }
            else conversation.PendingAdviceIds.RemoveAll(id => id == advice.Id);
        }
        return;

        void AddResult(int batch, ToolResult result)
        {
            var key = (batch, result.CallId);
            if (!calls.TryGetValue(key, out var call) || call.Name != result.Name ||
                result.Outcome == ToolOutcome.Deferred || !results.TryAdd(key, result))
                throw new ArgumentException("Result does not match a unique resolved call.", nameof(run));
        }
    }

    // Call under the same serialization as Synchronize and dispatch. The absolute batch
    // position is essential: call IDs may be reused by a later model turn.
    internal string RequireCurrentPendingCall(Guid adviceId, OrderedAgentRunResult run)
    {
        var advice = adviceService.Get(adviceId);
        var checkpoint = run.Checkpoint;
        var batch = run.Transcript.Count - 1;
        if (advice == null || advice.ConversationId != conversationId || advice.Status != AdviceStatus.Pending ||
            checkpoint == null || !_links.TryGetValue((batch, advice.ToolCallId ?? ""), out var link) ||
            link.AdviceId != adviceId || advice.ToolName != link.Call.Name ||
            !JToken.DeepEquals(JToken.FromObject(checkpoint.Transcript), JToken.FromObject(run.Transcript)) ||
            !checkpoint.Resolutions.Any(item => item.Result == null && SameCall(item.Call, link.Call)))
            throw new InvalidOperationException("Advice is not a current pending proposal in this session.");
        return link.Call.Id;
    }

    private static bool SameCall(ToolCall left, ToolCall right) => left.Id == right.Id &&
        left.Name == right.Name && JToken.DeepEquals(left.Arguments, right.Arguments);

    private static bool SameResult(ToolResult left, ToolResult right) => left.CallId == right.CallId &&
        left.Name == right.Name && left.Outcome == right.Outcome && left.Error == right.Error &&
        JToken.DeepEquals(left.Output, right.Output);
}
