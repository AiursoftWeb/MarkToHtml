using Aiursoft.AgentKit;
using ModelContextProtocol.Server;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.Services.Agent;

/// <summary>
/// Unregistered tool boundary for a future runner host. This does not invoke MCP tools.
/// The host owns user/document scope, legacy argument normalization, MCP text extraction,
/// and error-to-text conversion. Exceptions deliberately propagate unchanged.
/// </summary>
internal sealed class AgentToolAdapter : IAgentTool
{
    private readonly ToolDefinition _definition;
    private readonly Func<JToken, CancellationToken, ValueTask<string>> _execute;

    private AgentToolAdapter(McpServerTool tool, Func<JToken, CancellationToken, ValueTask<string>> execute)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(execute);
        var protocol = tool.ProtocolTool;
        _definition = new ToolDefinition(protocol.Name, protocol.Description!,
            JToken.Parse(protocol.InputSchema.GetRawText()), IsAdvice(tool));
        _execute = execute;
    }

    public static AgentToolAdapter ForReadOnly(McpServerTool tool,
        Func<JToken, CancellationToken, ValueTask<string>> read)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (IsAdvice(tool))
            throw new ArgumentException("Advice tools require an explicitly approved write handler.", nameof(tool));
        return new AgentToolAdapter(tool, read);
    }

    /// <summary>
    /// Bind only a host handler for an already approved operation, never DocumentEditTools:
    /// those methods propose edits rather than persist them. RequiresApproval is metadata,
    /// not a security gate; the runner/host must authorize before invoking ExecuteAsync.
    /// No approved write handler is implemented or connected in production yet.
    /// </summary>
    public static AgentToolAdapter ForApprovedWrite(McpServerTool tool,
        Func<JToken, CancellationToken, ValueTask<string>> approvedWrite)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (!IsAdvice(tool))
            throw new ArgumentException("An approved write handler requires an advice tool.", nameof(tool));
        return new AgentToolAdapter(tool, approvedWrite);
    }

    // ToolDefinition exposes a mutable schema, so each consumer receives its own snapshot.
    public ToolDefinition Definition => new(_definition.Name, _definition.Description,
        _definition.InputSchema, _definition.RequiresApproval);

    public async ValueTask<JToken> ExecuteAsync(JToken arguments, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await _execute(arguments.DeepClone(), cancellationToken);
        return new JValue(result);
    }

    private static bool IsAdvice(McpServerTool tool) => tool.Metadata.Any(metadata => metadata is AdviceAttribute);
}
