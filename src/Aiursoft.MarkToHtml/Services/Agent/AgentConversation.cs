namespace Aiursoft.MarkToHtml.Services.Agent;

public class AgentConversation
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string UserId { get; init; } = string.Empty;
    public Guid DocumentId { get; init; }
    public string DocumentContent { get; set; } = string.Empty;
    public string? DocumentTitle { get; set; }
    public AgentState State { get; set; } = AgentState.Thinking;
    public List<ToolMessagesItem> Messages { get; set; } = [];
    public List<Guid> PendingAdviceIds { get; set; } = [];
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public string? ErrorMessage { get; set; }
    public int LoopCount { get; set; }
}

public enum AgentState
{
    Thinking,
    AwaitingApproval,
    Completed,
    Error
}
