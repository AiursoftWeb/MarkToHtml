namespace Aiursoft.MarkToHtml.Models.AgentViewModels;

public class AgentStatusViewModel
{
    public Guid ConversationId { get; set; }
    public string State { get; set; } = string.Empty;
    public List<ChatMessageViewModel> Messages { get; set; } = [];
    public List<AdviceViewModel> PendingAdvice { get; set; } = [];
    public string? UpdatedDocumentContent { get; set; }
    public string? ErrorMessage { get; set; }
}
