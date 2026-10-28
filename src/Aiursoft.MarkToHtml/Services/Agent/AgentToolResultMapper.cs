using Aiursoft.AgentKit.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.Services.Agent;

/// <summary>
/// Tool result text projection with optional session-local safe failure feedback. The facade
/// serializes registration/dispatch/projection. Batch position plus call ID prevents feedback
/// leaking into later reused IDs. Canonical runner evidence is never rewritten.
/// </summary>
internal sealed class AgentToolResultMapper
{
    internal const string Rejected =
        "REJECTED: User rejected this change. Do NOT retry the same edit. Ask the user what they want instead.";

    private readonly Dictionary<(int Batch, string CallId), Failure> _failures = new();
    private (int Batch, string CallId, string Name)? _active;

    private sealed record Failure(string Code, string Message, string Recovery)
    {
        internal string Text => $"{Code}: {Message} {Recovery}";
    }

    internal void Begin(int batch, string callId, string name) => _active = (batch, callId, name);
    internal void End() => _active = null;

    internal void RecordWriteFailure(string name, bool persistenceStarted)
    {
        if (_active is not { } active || active.Name != name)
            throw new InvalidOperationException("Write failure has no active approved call.");
        _failures.Add((active.Batch, active.CallId), new Failure(
            persistenceStarted ? "DOCUMENT_PERSISTENCE_FAILED" : "WRITE_EXECUTION_FAILED",
            persistenceStarted
                ? "The document persistence stage failed after updating in-memory content. The saved document state is unknown."
                : "The approved write failed during execution. Persistence is not confirmed; in-memory content may already have changed.",
            "Do not blindly repeat this write. ReadFullDocument reads the current in-memory document, not proof of persistence. " +
            "Inspect that content before proposing a corrected edit; if persistence cannot be verified, ask the user to check or reload the saved document. Any new write requires approval."));
    }

    internal string GetText(int batch, ToolResult result) =>
        result.Outcome == ToolOutcome.Failed && _failures.TryGetValue((batch, result.CallId), out var failure)
            ? failure.Text
            : GetText(result);

    internal static string GetText(ToolResult result) => result.Outcome == ToolOutcome.Rejected
        ? Rejected
        : result.Output is JValue { Type: JTokenType.String } value
            ? value.Value<string>() ?? ""
            : result.Output?.ToString(Formatting.None) ?? result.Error ?? "";
}
