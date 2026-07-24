using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Models.AgentViewModels;
using Aiursoft.MarkToHtml.Services.Agent;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.Controllers;

[LimitPerMin]
[Authorize]
public class AgentController(
    IAgentService agentService,
    AdviceService adviceService,
    TemplateDbContext db,
    UserManager<User> userManager) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { Error = "Message is required." });

        var userId = userManager.GetUserId(User)!;

        // Continue existing conversation
        if (request.ConversationId.HasValue)
        {
            // Update document content before continuing
            var conversation = agentService.GetConversation(request.ConversationId.Value);
            if (conversation != null && !string.IsNullOrWhiteSpace(request.FullDocumentContent))
            {
                conversation.DocumentContent = request.FullDocumentContent;
            }

            var conversationId = agentService.ContinueRun(
                request.ConversationId.Value, userId, request.Message);
            if (conversationId == null)
                return BadRequest(new { Error = "Conversation not found, not yours, or still processing." });
            return Ok(new { ConversationId = conversationId.Value });
        }

        // Start new conversation - need document
        if (request.DocumentId == Guid.Empty)
            return BadRequest(new { Error = "Document ID is required for a new conversation." });

        // Verify document access
        var document = await db.MarkdownDocuments.FindAsync(request.DocumentId);
        if (document == null)
            return NotFound(new { Error = "Document not found." });

        var isOwner = document.UserId == userId;
        if (!isOwner)
        {
            var userRoles = await db.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();
            var canEdit = await db.DocumentShares
                .AnyAsync(s => s.DocumentId == request.DocumentId &&
                              s.Permission == SharePermission.Editable &&
                              (s.SharedWithUserId == userId ||
                               (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));
            if (!canEdit)
                return Forbid();
        }

        var documentContent = request.FullDocumentContent ?? document.Content ?? string.Empty;

        var newConversationId = await agentService.StartRun(
            userId, request.DocumentId, documentContent, document.Title, request.Message);
        return Ok(new { ConversationId = newConversationId });
    }

    [HttpGet]
    public IActionResult Status(Guid conversationId)
    {
        var conversation = agentService.GetConversation(conversationId);
        if (conversation == null)
            return NotFound(new { Error = "Conversation not found." });

        var userId = userManager.GetUserId(User)!;
        if (conversation.UserId != userId)
            return Forbid();

        var messages = conversation.Messages
            .Where(m => m.Role != "system" && !m.IsMeta)
            .Select(m => new ChatMessageViewModel
            {
                Role = m.Role ?? "unknown",
                Content = m.Content,
                ToolCalls = m.ToolCalls?.Select(tc => new ToolCallViewModel
                {
                    Id = tc.Id,
                    Name = tc.Function?.Name,
                    Arguments = tc.Function?.Arguments
                }).ToList(),
                ToolCallId = m.ToolCallId,
                IsMeta = m.IsMeta
            }).ToList();

        // Annotate tool_call messages with their advice status
        var pendingAdvice = adviceService.GetPendingForConversation(conversationId);
        foreach (var msg in messages.Where(m => m.ToolCalls?.Count > 0))
        {
            if (msg.ToolCalls == null) continue;
            foreach (var tc in msg.ToolCalls)
            {
                var matchingAdvice = pendingAdvice.FirstOrDefault(a => a.ToolCallId == tc.Id);
                if (matchingAdvice != null)
                {
                    msg.AdviceId = matchingAdvice.Id;
                    msg.AdviceStatus = matchingAdvice.Status.ToString();
                }
            }
        }

        var adviceViewModels = pendingAdvice.Select(a => new AdviceViewModel
        {
            AdviceId = a.Id,
            ToolDisplayName = a.ToolDisplayName,
            ParameterDisplay = a.ParameterDisplay,
            Status = a.Status.ToString(),
            DiffHunks = a.DiffHunks?.Select(h => new DiffHunkViewModel
            {
                OldStart = h.OldStart,
                OldLines = h.OldLines,
                NewStart = h.NewStart,
                NewLines = h.NewLines,
                Lines = h.Lines
            }).ToList(),
            OldString = a.Parameters.TryGetValue("oldString", out var oldStr) ? oldStr?.ToString() : null,
            NewString = a.Parameters.TryGetValue("newString", out var newStr) ? newStr?.ToString() : null,
            Parameters = a.DisplayParameters.Select(p => new ParameterItemViewModel
            {
                Key = p.Key,
                DisplayKey = p.DisplayKey,
                Value = p.Value
            }).ToList()
        }).ToList();

        // After approval, return the updated document content
        string? updatedContent = null;
        if (pendingAdvice.Count == 0 && conversation.State == AgentState.Completed)
        {
            updatedContent = agentService.GetUpdatedDocumentContent(conversationId);
        }

        return Ok(new AgentStatusViewModel
        {
            ConversationId = conversation.Id,
            State = conversation.State.ToString(),
            Messages = messages,
            PendingAdvice = adviceViewModels,
            UpdatedDocumentContent = updatedContent,
            ErrorMessage = conversation.ErrorMessage
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ApproveAdvice(Guid conversationId, Guid adviceId)
    {
        var conversation = agentService.GetConversation(conversationId);
        if (conversation == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (conversation.UserId != userId) return Forbid();

        agentService.ApproveAdvice(conversationId, adviceId);
        return Ok(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RejectAdvice(Guid conversationId, Guid adviceId)
    {
        var conversation = agentService.GetConversation(conversationId);
        if (conversation == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (conversation.UserId != userId) return Forbid();

        agentService.RejectAdvice(conversationId, adviceId);
        return Ok(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Cancel(Guid conversationId)
    {
        var conversation = agentService.GetConversation(conversationId);
        if (conversation == null) return NotFound();

        var userId = userManager.GetUserId(User)!;
        if (conversation.UserId != userId) return Forbid();

        agentService.CancelRun(conversationId);
        return Ok(new { success = true });
    }
}
