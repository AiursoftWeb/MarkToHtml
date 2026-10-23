using Aiursoft.AgentKit;
using Newtonsoft.Json.Linq;

namespace Aiursoft.MarkToHtml.Services.Agent;

/// <summary>
/// Opt-in real tool bindings, not registered in DI or used by AgentService. Supply a
/// long-lived scope factory and trusted conversation, never a request-scoped provider.
/// OrderedAgentSessionHost must gate approval before dispatching write handlers.
/// Legacy business errors and caught exceptions remain string outputs (JValue), so
/// runner Succeeded means dispatch returned, NOT business success. Evaluators must not
/// infer success from that outcome alone, just as on the legacy text-returning path.
/// Production rejection-text mapping remains a blocker; this factory does not implement Advice/UI state.
/// </summary>
internal static class AgentSessionToolFactory
{
    internal static IReadOnlyList<IAgentTool> Create(IServiceScopeFactory scopes,
        AgentConversation conversation, ToolRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(registry);

        return registry.AllTools.Select(tool =>
        {
            var isWrite = registry.IsWriteTool(tool.ProtocolTool.Name);
            async ValueTask<string> Execute(JToken arguments, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // Keep this provider alive across the await. The execution helper owns
                    // its nested MCP/persistence scopes; no provider is retained by the session.
                    using var scope = scopes.CreateScope();
                    var args = arguments.ToObject<Dictionary<string, object?>>()!;
                    return isWrite
                        ? await AgentToolExecution.ExecuteApprovedAsync(scope.ServiceProvider, scopes, tool, args, conversation)
                        : await AgentToolExecution.ExecuteMcpAsync(scope.ServiceProvider, tool, args, conversation);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return isWrite
                        ? $"Error executing tool: {ex.Message}"
                        : $"Error executing {tool.ProtocolTool.Name}: {ex.Message}";
                }
            }

            return (IAgentTool)(isWrite
                ? AgentToolAdapter.ForApprovedWrite(tool, Execute)
                : AgentToolAdapter.ForReadOnly(tool, Execute));
        }).ToArray();
    }
}
