namespace Aiursoft.MarkToHtml.Models.AgentViewModels;

public class ChatMessageViewModel
{
    public string Role { get; set; } = "unknown";
    public string? Content { get; set; }
    public List<ToolCallViewModel>? ToolCalls { get; set; }
    public string? ToolCallId { get; set; }
    public bool IsMeta { get; set; }
    public Guid? AdviceId { get; set; }
    public string? AdviceStatus { get; set; }
}
