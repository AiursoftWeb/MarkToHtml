using System.Collections.Concurrent;
using System.Text;
using Aiursoft.AgentKit;
using Aiursoft.Canon.TaskQueue;
using Aiursoft.MarkToHtml.Configuration;

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

    private readonly ConcurrentDictionary<Guid, AgentSessionFacade> _sessions = new();
    private readonly object _lifecycle = new();
    private static readonly TimeSpan ConversationTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan AdviceTtl = TimeSpan.FromMinutes(30);

    internal const string DocumentSystemReminder =
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
            task: async _ => await ExecuteSession(conversation.Id));

        return conversation.Id;
    }

    public Guid? ContinueRun(Guid conversationId, string userId, string userMessage)
    {
        lock (_lifecycle)
        {
            if (!_conversations.TryGetValue(conversationId, out var conversation) ||
                conversation.UserId != userId || string.IsNullOrWhiteSpace(userMessage) ||
                conversation.State is AgentState.Thinking or AgentState.AwaitingApproval ||
                !_sessions.TryGetValue(conversationId, out var session)) return null;
            conversation.State = AgentState.Thinking;
            conversation.LastActivity = DateTime.UtcNow;
            conversation.LoopCount = 0;
            var content = conversation.DocumentContent;
            _taskQueue.QueueWithDependency<IServiceProvider>("MarkToHtmlAgent", $"AgentContinue-{conversation.Id}",
                async _ =>
                {
                    if (_conversations.ContainsKey(conversationId))
                        await session.ContinueAsync(userId, userMessage, content);
                });
            return conversation.Id;
        }
    }

    public AgentConversation? GetConversation(Guid conversationId)
    {
        _conversations.TryGetValue(conversationId, out var conversation);
        return conversation;
    }

    public void ApproveAdvice(Guid conversationId, Guid adviceId)
    {
        QueueDecision(conversationId, adviceId, ApprovalDecision.Execute);
    }

    public void RejectAdvice(Guid conversationId, Guid adviceId) =>
        QueueDecision(conversationId, adviceId, ApprovalDecision.Reject);

    private void QueueDecision(Guid conversationId, Guid adviceId, ApprovalDecision decision)
    {
        lock (_lifecycle)
        {
            if (!_conversations.ContainsKey(conversationId) || !_sessions.TryGetValue(conversationId, out var session) ||
                !session.ReserveDecision(adviceId, decision)) return;
            _taskQueue.QueueWithDependency<IServiceProvider>("MarkToHtmlAgent", $"AdviceExecute-{adviceId}",
                async _ => await ExecuteDecision(conversationId, adviceId, decision));
        }
    }

    private async Task ExecuteDecision(Guid conversationId, Guid adviceId, ApprovalDecision decision)
    {
        if (!_conversations.ContainsKey(conversationId) || !_sessions.TryGetValue(conversationId, out var session)) return;
        try { await session.DispatchReservedAsync(adviceId, decision); }
        catch (Exception ex) { _logger.LogError(ex, "Agent decision failed for {ConversationId}", conversationId); }
    }

    public void CancelRun(Guid conversationId)
    {
        lock (_lifecycle)
        {
            if (_conversations.TryRemove(conversationId, out _))
            {
                _sessions.TryRemove(conversationId, out _);
                _adviceService.RemoveConversationAdvice(conversationId);
            }
        }
    }

    private async Task ExecuteSession(Guid conversationId)
    {
        AgentConversation? conversation = null;
        try
        {
            AgentSessionFacade session;
            lock (_lifecycle)
            {
                if (!_conversations.TryGetValue(conversationId, out conversation)) return;
                session = _sessions.GetOrAdd(conversationId, _ => new AgentSessionFacade(conversation,
                    _claudeClient, _scopeFactory, _toolRegistry, _adviceService));
            }
            await session.StartAsync();
        }
        catch (Exception ex)
        {
            if (conversation != null)
            {
                conversation.State = AgentState.Error;
                conversation.ErrorMessage = "Session execution failed.";
            }
            _logger.LogError(ex, "Agent session failed for {ConversationId}", conversationId);
        }
    }

    internal static string FormatDocumentWithLineNumbers(string content)
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

    internal static Advice CreateToolAdvice(AdviceService adviceService, ToolRegistry registry,
        AgentConversation conversation, string toolName, string? callId, Dictionary<string, object?> args)
    {
        var tool = registry.GetTool(toolName);
        var (displayText, displayParams) = BuildParameterDisplay(toolName, args);
        List<DiffHunk>? diffHunks = null;
        if (toolName == "ReplaceText" &&
            args.TryGetValue("oldString", out var oldObj) && args.TryGetValue("newString", out var newObj))
            diffHunks = ComputeDiffHunks(oldObj?.ToString() ?? "", newObj?.ToString() ?? "");
        return adviceService.Create(conversation.Id, toolName, tool?.ProtocolTool.Title ?? toolName,
            tool?.ProtocolTool.Description ?? "", args, displayText, callId, displayParams, diffHunks,
            conversation.DocumentContent);
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

    private void CleanupExpiredConversations()
    {
        var conversationCutoff = DateTime.UtcNow - ConversationTtl;
        var adviceCutoff = DateTime.UtcNow - AdviceTtl;

        foreach (var (id, conv) in _conversations)
        {
            if (conv.LastActivity < conversationCutoff) CancelRun(id);
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
