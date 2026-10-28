using Aiursoft.AgentKit.AgentRunner;

namespace Aiursoft.MarkToHtml.Services.Agent;

/// <summary>
/// Independent runtime capture for offline consumers. Run collections and nested JSON are
/// copied from the session, not fully immutable; consumer edits cannot change later captures.
/// TurnStartIndex is the trusted absolute boundary of the current turn's model completions.
/// DocumentContent is in-memory state, not proof of persistence.
/// </summary>
public sealed record AgentSessionRuntimeSnapshot(
    OrderedAgentRunResult Run,
    int TurnStartIndex,
    string DocumentContent,
    IReadOnlyCollection<string> AvailableTools);
