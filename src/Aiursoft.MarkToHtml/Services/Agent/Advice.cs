namespace Aiursoft.MarkToHtml.Services.Agent;

public class Advice
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ConversationId { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public string ToolDisplayName { get; init; } = string.Empty;
    public string ToolDescription { get; init; } = string.Empty;
    public Dictionary<string, object?> Parameters { get; init; } = new();
    public string ParameterDisplay { get; init; } = string.Empty;
    public string? ToolCallId { get; init; }
    public AdviceStatus Status { get; set; } = AdviceStatus.Pending;
    public string? Result { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>Structured key-value pairs for UI rendering (key, displayKey, value).</summary>
    public List<AdviceParameterItem> DisplayParameters { get; init; } = new();
    /// <summary>Pre-computed diff hunks for edit tool advice.</summary>
    public List<DiffHunk>? DiffHunks { get; set; }
    /// <summary>The original document content at advice creation time.</summary>
    public string? DocumentContentSnapshot { get; set; }
}

public class AdviceParameterItem
{
    public string Key { get; init; } = string.Empty;
    public string DisplayKey { get; init; } = string.Empty;
    public string? Value { get; init; }
}

/// <summary>
/// A single diff hunk, mirroring the structuredPatch format from Claude Code / diff library.
/// </summary>
public class DiffHunk
{
    public int OldStart { get; set; }
    public int OldLines { get; set; }
    public int NewStart { get; set; }
    public int NewLines { get; set; }
    public List<string> Lines { get; set; } = [];
}

public enum AdviceStatus
{
    Pending,
    Approved,
    Rejected,
    Executed,
    Failed
}
