using Aiursoft.MarkToHtml.Entities;
using ModelContextProtocol.Server;

namespace Aiursoft.MarkToHtml.Services.Agent;

/// <summary>
/// Internal execution seam shared by the legacy service and future adapter handlers.
/// Not registered in DI. Callers own approval, trusted conversation context and error conversion.
/// Exceptions deliberately propagate to the caller's existing boundary.
/// </summary>
internal static class AgentToolExecution
{
    /// <summary>
    /// Execute only after the caller has approved this operation. In particular, ReplaceText
    /// must use this path, not DocumentEditTools (which only proposes an edit).
    /// The separate scope factory preserves the legacy persistence scope.
    /// </summary>
    internal static async Task<string> ExecuteApprovedAsync(
        IServiceProvider services,
        IServiceScopeFactory persistenceScopes,
        McpServerTool tool,
        Dictionary<string, object?> args,
        AgentConversation conversation)
    {
        if (tool.ProtocolTool.Name == "ReplaceText" &&
            args.TryGetValue("oldString", out var oldObj) &&
            args.TryGetValue("newString", out var newObj))
        {
            var oldString = oldObj?.ToString() ?? "";
            var newString = newObj?.ToString() ?? "";
            var index = conversation.DocumentContent.IndexOf(oldString, StringComparison.Ordinal);
            if (index >= 0)
            {
                conversation.DocumentContent = conversation.DocumentContent[..index] +
                    newString +
                    conversation.DocumentContent[(index + oldString.Length)..];

                using var scope = persistenceScopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
                var document = await db.MarkdownDocuments.FindAsync(conversation.DocumentId);
                if (document != null)
                {
                    document.Content = conversation.DocumentContent;
                    document.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }

                conversation.DocumentContentUpdated = true;
                return $"Document updated successfully. {oldString.Split('\n').Length} lines replaced with {newString.Split('\n').Length} lines.";
            }

            return "Error: old_string not found in current document. The document may have been modified since the edit was proposed.";
        }

        return await ExecuteMcpAsync(services, tool, args, conversation);
    }

    internal static async Task<string> ExecuteMcpAsync(
        IServiceProvider services,
        McpServerTool tool,
        Dictionary<string, object?> args,
        AgentConversation conversation)
    {
        using var scope = services.CreateScope();
        var currentUser = scope.ServiceProvider.GetRequiredService<CurrentUserService>();
        currentUser.UserId = conversation.UserId;
        currentUser.DocumentContent = conversation.DocumentContent;
        currentUser.DocumentId = conversation.DocumentId;

        var jsonArgs = new Dictionary<string, System.Text.Json.JsonElement>();
        foreach (var (key, value) in args)
        {
            var sanitized = value is string s && s.Length == 0 ? null : value;
            var json = System.Text.Json.JsonSerializer.SerializeToElement(sanitized);
            jsonArgs[key] = json;
        }

        var requestParams = new ModelContextProtocol.Protocol.CallToolRequestParams
        {
            Name = tool.ProtocolTool.Name,
            Arguments = jsonArgs
        };
        var request = new RequestContext<ModelContextProtocol.Protocol.CallToolRequestParams>(
            server: NullMcpServer.Instance,
            jsonRpcRequest: new ModelContextProtocol.Protocol.JsonRpcRequest { Method = "tools/call" },
            parameters: requestParams)
        {
            Services = scope.ServiceProvider
        };

        var result = await tool.InvokeAsync(request);
        var textContent = result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().FirstOrDefault();
        return textContent?.Text ?? result.ToString() ?? "Tool executed.";
    }
}
