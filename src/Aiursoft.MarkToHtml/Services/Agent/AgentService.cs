using System.Collections.Concurrent;
using System.Text;
using Aiursoft.Canon.TaskQueue;
using Aiursoft.MarkToHtml.Configuration;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace Aiursoft.MarkToHtml.Services.Agent;

public class AgentService : IAgentService
{
    private readonly ConcurrentDictionary<Guid, AgentConversation> _conversations = new();
    private readonly ServiceTaskQueue _taskQueue;
    private readonly ToolRegistry _toolRegistry;
    private readonly AdviceService _adviceService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ClaudeClient _claudeClient;
    private readonly ILogger<AgentService> _logger;

    private const int MaxLoops = 10;
    private static readonly TimeSpan ConversationTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan AdviceTtl = TimeSpan.FromMinutes(30);

    private const string DocumentSystemReminder =
        "<system-reminder>\n" +
        "You are editing a Markdown document. The document content is provided with line number prefixes.\n" +
        "When using ReplaceText:\n" +
        "- old_string must match the document EXACTLY (copy from Read output, strip line number prefixes)\n" +
        "- old_string must be UNIQUE in the document — include more context if it matches multiple places\n" +
        "- new_string must be different from old_string\n" +
        "- Propose ONE edit per ReplaceText call. Make multiple calls for multiple changes.\n" +
        "- Read relevant sections before editing. After editing, verify with ReadDocumentLines.\n" +
        "</system-reminder>";

    public AgentService(
        ServiceTaskQueue taskQueue,
        ToolRegistry toolRegistry,
        AdviceService adviceService,
        ClaudeClient claudeClient,
        IServiceScopeFactory scopeFactory,
        ILogger<AgentService> logger)
    {
        _taskQueue = taskQueue;
        _toolRegistry = toolRegistry;
        _adviceService = adviceService;
        _claudeClient = claudeClient;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<Guid> StartRun(string userId, Guid documentId, string documentContent, string? documentTitle, string userMessage)
    {
        CleanupExpiredConversations();

        var conversation = new AgentConversation
        {
            UserId = userId,
            DocumentId = documentId,
            DocumentContent = documentContent,
            DocumentTitle = documentTitle
        };

        // System prompt
        using var settingsScope = _scopeFactory.CreateScope();
        var globalSettings = settingsScope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
        var systemPrompt = await globalSettings.GetSettingValueAsync(SettingsMap.AgentSystemPrompt);
        systemPrompt = systemPrompt
            .Replace("{documentTitle}", documentTitle ?? "Untitled")
            .Replace("{documentContentLength}", documentContent.Length.ToString());

        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "system",
            Content = systemPrompt
        });

        // Document reminder (meta, not visible in UI)
        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "user",
            Content = DocumentSystemReminder,
            IsMeta = true
        });

        // Full document content with line numbers (meta)
        var numberedDoc = FormatDocumentWithLineNumbers(documentContent);
        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "user",
            Content = $"<document-content>\n{numberedDoc}\n</document-content>\n\nCurrent document title: {documentTitle ?? "Untitled"}",
            IsMeta = true
        });

        // Actual user message
        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "user",
            Content = userMessage
        });

        _conversations[conversation.Id] = conversation;

        _taskQueue.QueueWithDependency<IServiceProvider>(
            queueName: "MarkToHtmlAgent",
            taskName: $"AgentRun-{conversation.Id}",
            task: async (sp) => await ExecuteReActLoop(sp, conversation.Id));

        return conversation.Id;
    }

    public Guid? ContinueRun(Guid conversationId, string userId, string userMessage)
    {
        if (!_conversations.TryGetValue(conversationId, out var conversation))
            return null;

        if (conversation.UserId != userId)
            return null;

        if (conversation.State is AgentState.Thinking or AgentState.AwaitingApproval)
            return null;

        if (string.IsNullOrWhiteSpace(userMessage))
            return null;

        // Inject current document content snapshot
        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "user",
            Content = DocumentSystemReminder,
            IsMeta = true
        });

        var numberedDoc = FormatDocumentWithLineNumbers(conversation.DocumentContent);
        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "user",
            Content = $"<document-content>\n{numberedDoc}\n</document-content>",
            IsMeta = true
        });

        conversation.Messages.Add(new ToolMessagesItem
        {
            Role = "user",
            Content = userMessage
        });

        conversation.State = AgentState.Thinking;
        conversation.LastActivity = DateTime.UtcNow;
        conversation.LoopCount = 0;

        _taskQueue.QueueWithDependency<IServiceProvider>(
            queueName: "MarkToHtmlAgent",
            taskName: $"AgentContinue-{conversation.Id}",
            task: async (sp) => await ExecuteReActLoop(sp, conversation.Id));

        return conversation.Id;
    }

    public AgentConversation? GetConversation(Guid conversationId)
    {
        _conversations.TryGetValue(conversationId, out var conversation);
        return conversation;
    }

    public void ApproveAdvice(Guid conversationId, Guid adviceId)
    {
        var advice = _adviceService.Get(adviceId);
        if (advice == null || advice.Status != AdviceStatus.Pending) return;

        _adviceService.UpdateStatus(adviceId, AdviceStatus.Approved);

        if (_conversations.TryGetValue(conversationId, out var conversation))
        {
            conversation.PendingAdviceIds.Remove(adviceId);
            conversation.LastActivity = DateTime.UtcNow;

            _taskQueue.QueueWithDependency<IServiceProvider>(
                queueName: "MarkToHtmlAgent",
                taskName: $"AdviceExecute-{adviceId}",
                task: async (sp) => await ExecuteAdviceAndResume(sp, conversationId, adviceId));
        }
    }

    public void RejectAdvice(Guid conversationId, Guid adviceId)
    {
        var advice = _adviceService.Get(adviceId);
        if (advice == null || advice.Status != AdviceStatus.Pending) return;

        _adviceService.UpdateStatus(adviceId, AdviceStatus.Rejected);

        if (_conversations.TryGetValue(conversationId, out var conversation))
        {
            conversation.PendingAdviceIds.Remove(adviceId);
            conversation.LastActivity = DateTime.UtcNow;

            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "tool",
                ToolCallId = advice.ToolCallId,
                Content = "REJECTED: User rejected this change. Do NOT retry the same edit. Ask the user what they want instead."
            });

            var stillPending = _adviceService.GetPendingForConversation(conversationId);
            if (stillPending.Count > 0)
            {
                conversation.State = AgentState.AwaitingApproval;
            }
            else
            {
                conversation.State = AgentState.Completed;
            }
        }
    }

    public void CancelRun(Guid conversationId)
    {
        if (_conversations.TryRemove(conversationId, out _))
        {
            _adviceService.RemoveConversationAdvice(conversationId);
        }
    }

    // ── ReAct Loop ───────────────────────────────────────────────────────────────

    private async Task ExecuteReActLoop(IServiceProvider sp, Guid conversationId)
    {
        if (!_conversations.TryGetValue(conversationId, out var conversation)) return;

        try
        {
            while (conversation.LoopCount < MaxLoops)
            {
                conversation.LoopCount++;
                conversation.State = AgentState.Thinking;
                conversation.LastActivity = DateTime.UtcNow;

                var response = await CallLlmWithTools(conversation);

                var toolUses = response.GetToolUses();
                if (toolUses.Count > 0)
                {
                    var assistantToolCalls = toolUses.Select(tu => new ToolCallData
                    {
                        Id = tu.Id,
                        Type = "function",
                        Function = new ToolCallFunction
                        {
                            Name = tu.Name,
                            Arguments = JsonConvert.SerializeObject(UnwrapJsonElements(tu.Input ?? new()))
                        }
                    }).ToList();

                    conversation.Messages.Add(new ToolMessagesItem
                    {
                        Role = "assistant",
                        Content = response.GetText(),
                        ToolCalls = assistantToolCalls,
                        ReasoningContent = response.ReasoningContent
                    });

                    var adviceIds = new List<Guid>();

                    foreach (var tu in toolUses)
                    {
                        if (string.IsNullOrEmpty(tu.Name)) continue;
                        var isWrite = _toolRegistry.IsWriteTool(tu.Name);

                        if (isWrite)
                        {
                            var tool = _toolRegistry.GetTool(tu.Name);
                            var displayName = tool?.ProtocolTool.Title ?? tu.Name;
                            var args = tu.Input ?? new Dictionary<string, object?>();
                            var (displayText, displayParams) = BuildParameterDisplay(tu.Name, args);

                            // Pre-compute diff hunks for edit tools
                            List<DiffHunk>? diffHunks = null;
                            if (tu.Name == "ReplaceText" &&
                                args.TryGetValue("oldString", out var oldObj) &&
                                args.TryGetValue("newString", out var newObj))
                            {
                                var oldString = oldObj?.ToString() ?? "";
                                var newString = newObj?.ToString() ?? "";
                                diffHunks = ComputeDiffHunks(oldString, newString);
                            }

                            var advice = _adviceService.Create(
                                conversationId: conversationId,
                                toolName: tu.Name,
                                toolDisplayName: displayName,
                                toolDescription: tool?.ProtocolTool.Description ?? "",
                                parameters: args,
                                parameterDisplay: displayText,
                                toolCallId: tu.Id,
                                displayParameters: displayParams,
                                diffHunks: diffHunks,
                                documentContentSnapshot: conversation.DocumentContent);

                            adviceIds.Add(advice.Id);
                            _logger.LogInformation("Advice created: {AdviceId} for tool {ToolName}", advice.Id, tu.Name);
                        }
                        else
                        {
                            string result;
                            try
                            {
                                result = await ExecuteTool(sp, tu, conversation.UserId);
                                _logger.LogInformation("Read tool executed: {ToolName}", tu.Name);
                            }
                            catch (Exception ex)
                            {
                                result = $"Error executing {tu.Name}: {ex.Message}";
                                _logger.LogWarning(ex, "Read tool failed: {ToolName}", tu.Name);
                            }
                            conversation.Messages.Add(new ToolMessagesItem
                            {
                                Role = "tool",
                                ToolCallId = tu.Id,
                                Content = result
                            });
                        }
                    }

                    if (adviceIds.Count > 0)
                    {
                        conversation.PendingAdviceIds.AddRange(adviceIds);
                        conversation.State = AgentState.AwaitingApproval;
                        return;
                    }

                    continue;
                }

                // Text response, conversation complete
                var text = response.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    conversation.Messages.Add(new ToolMessagesItem
                    {
                        Role = "assistant",
                        Content = text,
                        ReasoningContent = response.ReasoningContent
                    });
                }

                conversation.State = AgentState.Completed;
                return;
            }

            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "assistant",
                Content = "I've reached the maximum number of steps. Please refine your request or approve pending actions."
            });
            conversation.State = AgentState.Completed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent ReAct loop failed for conversation {ConversationId}", conversationId);
            conversation.State = AgentState.Error;
            conversation.ErrorMessage = ex.Message;
        }
    }

    private async Task ExecuteAdviceAndResume(IServiceProvider sp, Guid conversationId, Guid adviceId)
    {
        if (!_conversations.TryGetValue(conversationId, out var conversation)) return;

        var advice = _adviceService.Get(adviceId);
        if (advice == null || advice.Status != AdviceStatus.Approved) return;

        try
        {
            var tool = _toolRegistry.GetTool(advice.ToolName);
            if (tool == null)
            {
                _adviceService.SetResult(adviceId, null, $"Tool not found: {advice.ToolName}");
                return;
            }

            var args = new Dictionary<string, object?>(advice.Parameters);

            // For ReplaceText: apply the edit to the in-memory document content
            string result;
            if (advice.ToolName == "ReplaceText" &&
                args.TryGetValue("oldString", out var oldObj) &&
                args.TryGetValue("newString", out var newObj))
            {
                var oldString = oldObj?.ToString() ?? "";
                var newString = newObj?.ToString() ?? "";

                // Apply the replacement
                var index = conversation.DocumentContent.IndexOf(oldString, StringComparison.Ordinal);
                if (index >= 0)
                {
                    conversation.DocumentContent = conversation.DocumentContent[..index] +
                        newString +
                        conversation.DocumentContent[(index + oldString.Length)..];

                    // Persist to DB
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
                    var document = await db.MarkdownDocuments.FindAsync(conversation.DocumentId);
                    if (document != null)
                    {
                        document.Content = conversation.DocumentContent;
                        document.UpdatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync();
                    }

                    result = $"Document updated successfully. {oldString.Split('\n').Length} lines replaced with {newString.Split('\n').Length} lines.";
                }
                else
                {
                    result = "Error: old_string not found in current document. The document may have been modified since the edit was proposed.";
                }
            }
            else
            {
                result = await ExecuteToolWithArgs(sp, tool, args, conversation.UserId);
            }

            _adviceService.SetResult(adviceId, result, null);

            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "tool",
                ToolCallId = advice.ToolCallId,
                Content = result
            });

            // Update document context for next steps
            var stillPending = _adviceService.GetPendingForConversation(conversationId);
            if (stillPending.Count > 0)
            {
                conversation.State = AgentState.AwaitingApproval;
            }
            else
            {
                conversation.State = AgentState.Thinking;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Advice execution failed for {AdviceId}", adviceId);
            _adviceService.SetResult(adviceId, null, ex.Message);

            conversation.Messages.Add(new ToolMessagesItem
            {
                Role = "tool",
                ToolCallId = advice.ToolCallId,
                Content = $"Error executing tool: {ex.Message}"
            });

            var stillPending = _adviceService.GetPendingForConversation(conversationId);
            if (stillPending.Count > 0)
            {
                conversation.State = AgentState.AwaitingApproval;
                return;
            }
        }

        await ExecuteReActLoop(sp, conversationId);
    }

    // ── LLM Communication ──────────────────────────────────────────────────────

    private async Task<ClaudeResponse> CallLlmWithTools(AgentConversation conversation)
    {
        var systemPrompt = conversation.Messages
            .Where(m => m.Role == "system")
            .Select(m => m.Content)
            .FirstOrDefault() ?? "";

        var claudeMessages = ConvertToClaudeMessages(conversation.Messages);
        var tools = BuildClaudeTools();

        return await _claudeClient.SendAsync(systemPrompt, claudeMessages, tools);
    }

    private static List<ClaudeMessage> ConvertToClaudeMessages(List<ToolMessagesItem> messages)
    {
        var result = new List<ClaudeMessage>();

        foreach (var msg in messages.Where(m => m.Role != "system"))
        {
            if (msg.Role == "user")
            {
                result.Add(ClaudeMessage.User(msg.Content ?? ""));
            }
            else if (msg.Role == "assistant")
            {
                var blocks = new List<ClaudeContentBlock>();

                if (!string.IsNullOrWhiteSpace(msg.Content))
                    blocks.Add(ClaudeContentBlock.TextBlock(msg.Content));

                if (msg.ToolCalls != null)
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        var input = TryParseArgs(tc.Function?.Arguments ?? "{}");
                        blocks.Add(ClaudeContentBlock.ToolUse(
                            tc.Id ?? Guid.NewGuid().ToString(),
                            tc.Function?.Name ?? "",
                            input));
                    }
                }

                result.Add(ClaudeMessage.Assistant(blocks, msg.ReasoningContent));
            }
            else if (msg.Role == "tool")
            {
                result.Add(ClaudeMessage.ToolResult(msg.ToolCallId ?? "", msg.Content ?? ""));
            }
        }

        return result;
    }

    private List<ClaudeTool> BuildClaudeTools()
    {
        return _toolRegistry.AllTools.Select(tool =>
        {
            var proto = tool.ProtocolTool;
            return new ClaudeTool
            {
                Name = proto.Name,
                Description = proto.Description,
                InputSchema = System.Text.Json.JsonSerializer.Deserialize<object>(proto.InputSchema.GetRawText())!
            };
        }).ToList();
    }

    // ── Tool Execution ──────────────────────────────────────────────────────────

    private async Task<string> ExecuteTool(IServiceProvider sp, ClaudeContentBlock toolUse, string userId)
    {
        var tool = _toolRegistry.GetTool(toolUse.Name ?? "");
        if (tool == null) return $"Error: Unknown tool '{toolUse.Name}'.";

        var args = UnwrapJsonElements(toolUse.Input ?? new());
        return await ExecuteToolWithArgs(sp, tool, args, userId);
    }

    private async Task<string> ExecuteToolWithArgs(IServiceProvider sp, McpServerTool tool, Dictionary<string, object?> args, string userId)
    {
        using var scope = sp.CreateScope();

        var currentUser = scope.ServiceProvider.GetRequiredService<CurrentUserService>();
        currentUser.UserId = userId;

        // Set document content from conversation for read tools
        var toolName = tool.ProtocolTool.Name;
        if (toolName == "ReadFullDocument" || toolName == "ReadDocumentLines")
        {
            // Find the conversation for this user to get document content
            var conv = _conversations.Values
                .FirstOrDefault(c => c.UserId == userId && c.State != AgentState.Error);
            if (conv != null)
            {
                currentUser.DocumentContent = conv.DocumentContent;
                currentUser.DocumentId = conv.DocumentId;
            }
        }

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

    // ── Utilities ────────────────────────────────────────────────────────────────

    private static string FormatDocumentWithLineNumbers(string content)
    {
        var lines = content.Split('\n');
        var sb = new StringBuilder();
        var width = lines.Length.ToString().Length;
        for (var i = 0; i < lines.Length; i++)
        {
            sb.Append((i + 1).ToString().PadLeft(width));
            sb.Append('|');
            sb.AppendLine(lines[i]);
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// Computes unified diff hunks from old_string vs new_string.
    /// Uses a simple line-based diff algorithm.
    /// </summary>
    private static List<DiffHunk> ComputeDiffHunks(string oldString, string newString)
    {
        var oldLines = oldString.Split('\n');
        var newLines = newString.Split('\n');
        var hunks = new List<DiffHunk>();

        // Simple diff: find common prefix and suffix, the middle is the change
        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length &&
               oldLines[prefix] == newLines[prefix])
            prefix++;

        var suffix = 0;
        while (suffix < oldLines.Length - prefix && suffix < newLines.Length - prefix &&
               oldLines[oldLines.Length - 1 - suffix] == newLines[newLines.Length - 1 - suffix])
            suffix++;

        var oldChangedStart = prefix;
        var oldChangedCount = oldLines.Length - prefix - suffix;
        var newChangedStart = prefix;
        var newChangedCount = newLines.Length - prefix - suffix;

        // Context lines (up to 3)
        var contextBefore = Math.Min(prefix, 3);
        var hunkOldStart = oldChangedStart - contextBefore + 1;
        var hunkNewStart = newChangedStart - contextBefore + 1;

        var hunkLines = new List<string>();

        // Context before
        for (var i = oldChangedStart - contextBefore; i < oldChangedStart; i++)
            hunkLines.Add($" {oldLines[i]}");

        // Removed lines
        for (var i = 0; i < oldChangedCount; i++)
            hunkLines.Add($"-{oldLines[oldChangedStart + i]}");

        // Added lines
        for (var i = 0; i < newChangedCount; i++)
            hunkLines.Add($"+{newLines[newChangedStart + i]}");

        // Context after
        for (var i = 0; i < Math.Min(suffix, 3); i++)
            hunkLines.Add($" {oldLines[oldChangedStart + oldChangedCount + i]}");

        hunks.Add(new DiffHunk
        {
            OldStart = hunkOldStart,
            OldLines = contextBefore + oldChangedCount + Math.Min(suffix, 3),
            NewStart = hunkNewStart,
            NewLines = contextBefore + newChangedCount + Math.Min(suffix, 3),
            Lines = hunkLines
        });

        return hunks;
    }

    private static (string DisplayText, List<AdviceParameterItem> Parameters) BuildParameterDisplay(
        string toolName, Dictionary<string, object?> args)
    {
        var friendlyName = toolName switch
        {
            "ReplaceText" => "Edit Document",
            _ => toolName
        };

        var displayParams = new List<AdviceParameterItem>();
        foreach (var (key, value) in args)
        {
            var displayKey = key switch
            {
                "oldString" => "Replace",
                "newString" => "With",
                _ => key
            };
            var displayValue = value?.ToString() ?? "";
            // Truncate long values
            if (displayValue.Length > 100)
                displayValue = displayValue[..100] + "...";
            displayParams.Add(new AdviceParameterItem
            {
                Key = key,
                DisplayKey = displayKey,
                Value = displayValue
            });
        }

        var flatParams = displayParams.Select(p => $"{p.DisplayKey}: {p.Value}");
        var displayText = $"{friendlyName} ({string.Join(", ", flatParams)})";

        return (displayText, displayParams);
    }

    private static Dictionary<string, object?> UnwrapJsonElements(Dictionary<string, object?> args)
    {
        var result = new Dictionary<string, object?>();
        foreach (var (key, value) in args)
        {
            result[key] = value switch
            {
                System.Text.Json.JsonElement el => UnwrapJsonElement(el),
                _ => value
            };
        }
        return result;
    }

    private static object? UnwrapJsonElement(System.Text.Json.JsonElement el)
    {
        return el.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => el.GetString(),
            System.Text.Json.JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.Null => null,
            _ => el.GetRawText()
        };
    }

    private static Dictionary<string, object?> TryParseArgs(string json)
    {
        try
        {
            var dict = JsonConvert.DeserializeObject<Dictionary<string, object?>>(json) ?? new();
            return UnwrapJsonElements(dict);
        }
        catch
        {
            return new();
        }
    }

    private void CleanupExpiredConversations()
    {
        var conversationCutoff = DateTime.UtcNow - ConversationTtl;
        var adviceCutoff = DateTime.UtcNow - AdviceTtl;

        foreach (var (id, conv) in _conversations)
        {
            if (conv.LastActivity < conversationCutoff && _conversations.TryRemove(id, out _))
                _adviceService.RemoveConversationAdvice(id);
        }

        _adviceService.RemoveExpiredAdvice(adviceCutoff);
    }

    /// <summary>
    /// Returns the updated document content after an approved edit.
    /// Called by the controller to return to the UI after approval.
    /// </summary>
    public string? GetUpdatedDocumentContent(Guid conversationId)
    {
        _conversations.TryGetValue(conversationId, out var conversation);
        return conversation?.DocumentContent;
    }
}
