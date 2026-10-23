using Aiursoft.AgentKit;
using Aiursoft.AgentKit.AgentRunner;
using Aiursoft.AgentKit.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.Services.Agent;

/// <summary>
/// Opt-in history boundary only; not wired to AgentService, DI or UI.
/// Legacy history has no ToolOutcome: import is lossy and only for completed initial history,
/// never evaluation evidence or a round-trip of runner state. Evaluate canonical results instead.
/// UI projections (especially partial batches) must never be fed back to the same model adapter.
/// </summary>
internal static class AgentHistoryMapper
{
    internal static (IReadOnlyList<TranscriptMessage> Transcript, IReadOnlyDictionary<int, string?> InitialReasoning)
        ImportCompletedLegacyHistoryLossy(IEnumerable<ToolMessagesItem> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        var transcript = new List<TranscriptMessage>();
        var reasoning = new Dictionary<int, string?>();
        var pending = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in history)
        {
            var role = item.Role switch
            {
                "system" => TranscriptRole.System,
                "user" => TranscriptRole.User,
                "assistant" => TranscriptRole.Assistant,
                "tool" => TranscriptRole.Tool,
                _ => throw new NotSupportedException($"Unsupported legacy role '{item.Role}'.")
            };
            var content = new List<AgentContentBlock>();
            if (role == TranscriptRole.Tool)
            {
                RequireId(item.ToolCallId);
                if (!pending.Remove(item.ToolCallId!, out var name))
                    throw new ArgumentException("Legacy tool result has no matching unresolved call.", nameof(history));
                // Succeeded is an import placeholder, NOT a recovered outcome. Never infer from text.
                content.Add(new ToolResultBlock(new ToolResult(item.ToolCallId!, name, ToolOutcome.Succeeded,
                    new JValue(item.Content ?? ""))));
            }
            else
            {
                content.Add(new TextBlock(item.Content ?? ""));
                if (item.ToolCalls is { Count: > 0 })
                {
                    if (role != TranscriptRole.Assistant)
                        throw new NotSupportedException("Only assistant messages can contain legacy tool calls.");
                    foreach (var call in item.ToolCalls)
                    {
                        RequireId(call.Id);
                        if (string.IsNullOrWhiteSpace(call.Function?.Name))
                            throw new NotSupportedException("Legacy tool calls require a nonblank name.");
                        if (call.Type is not (null or "function"))
                            throw new NotSupportedException("Only legacy function calls are supported.");
                        if (!pending.TryAdd(call.Id!, call.Function.Name))
                            throw new ArgumentException("Duplicate unresolved legacy tool call ID.", nameof(history));
                        content.Add(new ToolCallBlock(new ToolCall(call.Id!, call.Function.Name,
                            ParseLegacyArguments(call.Function.Arguments))));
                    }
                }
            }
            if (role == TranscriptRole.Assistant && item.ReasoningContent != null)
                reasoning.Add(transcript.Count, item.ReasoningContent);
            transcript.Add(new TranscriptMessage(role, content, item.IsMeta));
        }
        TranscriptValidator.Validate(transcript);
        return (transcript.ToArray(), reasoning);
    }

    // Returns a fresh independent list, not an update to AgentConversation.Messages.
    // Absolute canonical indices are used BEFORE any UI-only hiding or tool-result expansion.
    internal static List<ToolMessagesItem> ProjectForUi(IReadOnlyList<TranscriptMessage> transcript,
        IReadOnlyDictionary<int, string?> reasoning)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(reasoning);
        foreach (var index in reasoning.Keys)
            if (index < 0 || index >= transcript.Count || transcript[index].Role != TranscriptRole.Assistant)
                throw new ArgumentException("Reasoning indices must identify canonical assistant messages.", nameof(reasoning));
        var projected = new List<ToolMessagesItem>();
        for (var index = 0; index < transcript.Count; index++)
        {
            var message = transcript[index];
            if (message.Role == TranscriptRole.Tool)
            {
                projected.AddRange(message.ToolResults.Select(result => ProjectResult(result, message.IsMeta)));
                continue;
            }
            var text = string.Concat(message.Content.OfType<TextBlock>().Select(block => block.Text));
            var calls = message.ToolCalls;
            if (message.Role == TranscriptRole.Assistant && calls.Count == 0 && string.IsNullOrWhiteSpace(text))
                continue; // Legacy UI behavior only. Canonical history and adapter indices remain unchanged.
            reasoning.TryGetValue(index, out var thought);
            projected.Add(new ToolMessagesItem
            {
                Role = message.Role.ToString().ToLowerInvariant(), Content = text, IsMeta = message.IsMeta,
                ReasoningContent = thought,
                ToolCalls = calls.Count == 0 ? null : calls.Select(call => new ToolCallData
                {
                    Id = call.Id,
                    Function = new ToolCallFunction { Name = call.Name, Arguments = call.Arguments.ToString(Formatting.None) }
                }).ToList()
            });
        }
        return projected;
    }

    internal static List<ToolMessagesItem> ProjectForUi(OrderedAgentRunResult result,
        IReadOnlyDictionary<int, string?> reasoning)
    {
        ArgumentNullException.ThrowIfNull(result);
        var projected = ProjectForUi(result.Transcript, reasoning);
        // Prior completed batches are already in Transcript. Only unresolved current-batch
        // resolutions carry results absent from it; CompletedResults also includes prior batches.
        if (result.Checkpoint is { } checkpoint)
            projected.AddRange(checkpoint.Resolutions.Where(item => item.Result != null)
                .Select(item => ProjectResult(item.Result!, false)));
        return projected;
    }

    private static ToolMessagesItem ProjectResult(ToolResult result, bool isMeta) => new()
    {
        Role = "tool", ToolCallId = result.CallId, Content = AgentToolResultText.GetText(result), IsMeta = isMeta
    };

    private static void RequireId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new NotSupportedException("Legacy history requires nonblank tool IDs; IDs are never synthesized. This mapper is not enabled in production.");
    }

    private static JObject ParseLegacyArguments(string? json)
    {
        try
        {
            // Match legacy TryParseArgs: malformed/non-object arguments become {}, not a new error.
            var arguments = JsonConvert.DeserializeObject<Dictionary<string, object?>>(json ?? "{}");
            return arguments == null ? new JObject() : JObject.FromObject(arguments);
        }
        catch (JsonException)
        {
            return new JObject();
        }
    }
}
