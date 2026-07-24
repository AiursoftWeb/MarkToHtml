namespace Aiursoft.MarkToHtml.Services.Agent;

public interface IAgentService
{
    Task<Guid> StartRun(string userId, Guid documentId, string documentContent, string? documentTitle, string userMessage);
    Guid? ContinueRun(Guid conversationId, string userId, string userMessage);
    AgentConversation? GetConversation(Guid conversationId);
    void ApproveAdvice(Guid conversationId, Guid adviceId);
    void RejectAdvice(Guid conversationId, Guid adviceId);
    void CancelRun(Guid conversationId);
    string? GetUpdatedDocumentContent(Guid conversationId);
}
