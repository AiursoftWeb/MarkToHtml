using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.MarkToHtml.Services.Agent;

/// <summary>
/// Carries the authenticated user identity and current document context during agent tool execution.
///
/// This service is injected into tool method signatures in place of explicit
/// <c>string userId</c> or <c>Guid documentId</c> parameters. Because it is registered in DI,
/// the MCP SDK automatically excludes it from the tool's JSON Schema.
///
/// AgentService sets these properties on the scoped instance before each tool invocation.
/// </summary>
public class CurrentUserService : IScopedDependency
{
    public string UserId { get; set; } = string.Empty;
    public Guid DocumentId { get; set; }
    public string DocumentContent { get; set; } = string.Empty;
}
