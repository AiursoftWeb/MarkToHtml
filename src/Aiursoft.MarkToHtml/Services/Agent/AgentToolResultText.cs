using Aiursoft.AgentKit.Messages;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.Services.Agent;

internal static class AgentToolResultText
{
    internal const string Rejected =
        "REJECTED: User rejected this change. Do NOT retry the same edit. Ask the user what they want instead.";

    // Project at the application boundary; do not rewrite the runner's correlated evidence.
    internal static string GetText(ToolResult result) => result.Outcome == ToolOutcome.Rejected
        ? Rejected
        : result.Output is JValue { Type: JTokenType.String } value
            ? value.Value<string>() ?? ""
            : result.Output?.ToString(Formatting.None) ?? result.Error ?? "";
}
