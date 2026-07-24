namespace Aiursoft.MarkToHtml.Models.AgentViewModels;

public class SendMessageRequest
{
    public Guid DocumentId { get; set; }
    public string Message { get; set; } = string.Empty;
    public Guid? ConversationId { get; set; }
    public string? FullDocumentContent { get; set; }
    public int? CursorLine { get; set; }
    public string? CursorContext { get; set; }
    public string? SelectedText { get; set; }
}
