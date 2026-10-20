using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Aiursoft.MarkToHtml.Controllers;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Models.AgentViewModels;
using Aiursoft.MarkToHtml.Services.Agent;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class AgentSecurityTests
{
    [TestMethod]
    public async Task SendMessage_OtherUsersConversation_DoesNotMutateContentOrContinue()
    {
        var conversation = CreateConversation("owner", AgentState.Completed);
        await AssertContinuationRejected(conversation, "other-user");
    }

    [TestMethod]
    [DataRow(AgentState.Thinking)]
    [DataRow(AgentState.AwaitingApproval)]
    public async Task SendMessage_BusyConversation_DoesNotMutateContentOrContinue(AgentState state)
    {
        var conversation = CreateConversation("owner", state);
        await AssertContinuationRejected(conversation, "owner");
    }

    [TestMethod]
    public async Task SendMessage_MissingConversation_DoesNotContinue()
    {
        var agent = new FakeAgentService(null);
        using var users = CreateUserManager();
        var controller = CreateController(agent, users, "owner");

        var result = await controller.SendMessage(new SendMessageRequest
        {
            ConversationId = Guid.NewGuid(),
            Message = "Continue editing",
            FullDocumentContent = "Untrusted editor content"
        });

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        Assert.AreEqual(0, agent.ContinueCalls);
    }

    [TestMethod]
    [DataRow(AgentState.Completed, "Updated editor content")]
    [DataRow(AgentState.Error, "Updated editor content")]
    [DataRow(AgentState.Completed, null)]
    [DataRow(AgentState.Completed, "   ")]
    public async Task SendMessage_OwnIdleConversation_PreservesContinuationBehavior(
        AgentState state, string? editorContent)
    {
        var conversation = CreateConversation("owner", state);
        var originalContent = conversation.DocumentContent;
        var agent = new FakeAgentService(conversation);
        using var users = CreateUserManager();
        var controller = CreateController(agent, users, "owner");

        var result = await controller.SendMessage(new SendMessageRequest
        {
            ConversationId = conversation.Id,
            Message = "Continue editing",
            FullDocumentContent = editorContent
        });

        Assert.IsInstanceOfType<OkObjectResult>(result);
        var response = JsonSerializer.SerializeToElement(((OkObjectResult)result).Value);
        Assert.AreEqual(conversation.Id, response.GetProperty("ConversationId").GetGuid());
        Assert.AreEqual(1, agent.ContinueCalls);
        Assert.AreEqual(conversation.Id, agent.ContinuedId);
        Assert.AreEqual("owner", agent.ContinuedUserId);
        Assert.AreEqual("Continue editing", agent.ContinuedMessage);
        var expectedContent = string.IsNullOrWhiteSpace(editorContent) ? originalContent : editorContent;
        Assert.AreEqual(expectedContent, conversation.DocumentContent);
        Assert.AreEqual(expectedContent, agent.ContentWhenContinued,
            "The editor snapshot must be updated before ContinueRun is called.");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Advice_OtherConversation_DoesNotMutateAdviceOrEitherConversation(bool approve)
    {
        var ownerConversation = CreateConversation("owner", AgentState.AwaitingApproval);
        var otherConversation = CreateConversation("other-user", AgentState.AwaitingApproval);
        var adviceService = new AdviceService();
        var advice = CreateAdvice(adviceService, ownerConversation);
        CreateAdvice(adviceService, otherConversation);
        var service = CreateAgentService(adviceService, ownerConversation, otherConversation);
        var adviceBefore = JsonSerializer.Serialize(advice);
        var ownerBefore = JsonSerializer.Serialize(ownerConversation);
        var otherBefore = JsonSerializer.Serialize(otherConversation);

        DecideAdvice(service, otherConversation.Id, advice.Id, approve);

        Assert.AreEqual(adviceBefore, JsonSerializer.Serialize(advice));
        Assert.AreEqual(ownerBefore, JsonSerializer.Serialize(ownerConversation));
        Assert.AreEqual(otherBefore, JsonSerializer.Serialize(otherConversation));
        Assert.AreSame(advice, adviceService.Get(advice.Id));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Advice_MissingConversation_DoesNotMutateAdvice(bool approve)
    {
        var removedConversation = CreateConversation("owner", AgentState.AwaitingApproval);
        var adviceService = new AdviceService();
        var advice = CreateAdvice(adviceService, removedConversation);
        // Deliberately retain the advice but do not register its conversation.
        var service = CreateAgentService(adviceService);
        var before = JsonSerializer.Serialize(advice);

        DecideAdvice(service, removedConversation.Id, advice.Id, approve);

        Assert.AreEqual(before, JsonSerializer.Serialize(advice));
        Assert.AreSame(advice, adviceService.Get(advice.Id));
        Assert.IsNull(service.GetConversation(removedConversation.Id));
    }

    [TestMethod]
    public async Task ExecuteAdviceAndResume_OtherConversation_DoesNotExecuteApprovedAdvice()
    {
        var ownerConversation = CreateConversation("owner", AgentState.AwaitingApproval);
        var otherConversation = CreateConversation("other-user", AgentState.AwaitingApproval);
        var adviceService = new AdviceService();
        var advice = CreateAdvice(adviceService, ownerConversation);
        adviceService.UpdateStatus(advice.Id, AdviceStatus.Approved);
        var service = CreateAgentService(adviceService, ownerConversation, otherConversation);
        var adviceBefore = JsonSerializer.Serialize(advice);
        var ownerBefore = JsonSerializer.Serialize(ownerConversation);
        var otherBefore = JsonSerializer.Serialize(otherConversation);
        var execute = typeof(AgentService).GetMethod("ExecuteAdviceAndResume",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(execute);

        // Invoke the queued callback directly: no task runner, tools or model are started.
        await (Task)execute.Invoke(service, new object?[] { null, otherConversation.Id, advice.Id })!;

        Assert.AreEqual(adviceBefore, JsonSerializer.Serialize(advice));
        Assert.AreEqual(ownerBefore, JsonSerializer.Serialize(ownerConversation));
        Assert.AreEqual(otherBefore, JsonSerializer.Serialize(otherConversation));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RejectAdvice_OwnConversation_RejectsAndUpdatesPendingState(bool hasOtherPendingAdvice)
    {
        var conversation = CreateConversation("owner", AgentState.AwaitingApproval);
        var adviceService = new AdviceService();
        var advice = CreateAdvice(adviceService, conversation);
        var remaining = hasOtherPendingAdvice ? CreateAdvice(adviceService, conversation) : null;
        var service = CreateAgentService(adviceService, conversation);
        var messagesBefore = conversation.Messages.ToArray();
        var contentBefore = conversation.DocumentContent;
        var lastActivityBefore = conversation.LastActivity;
        var beforeCall = DateTime.UtcNow;

        service.RejectAdvice(conversation.Id, advice.Id);

        Assert.AreEqual(AdviceStatus.Rejected, advice.Status);
        Assert.IsFalse(conversation.PendingAdviceIds.Contains(advice.Id));
        Assert.AreEqual(hasOtherPendingAdvice ? AgentState.AwaitingApproval : AgentState.Completed,
            conversation.State);
        Assert.IsTrue(conversation.LastActivity > lastActivityBefore);
        Assert.IsTrue(conversation.LastActivity >= beforeCall);
        Assert.IsTrue(conversation.LastActivity <= DateTime.UtcNow);
        Assert.AreEqual(contentBefore, conversation.DocumentContent);
        Assert.AreEqual(messagesBefore.Length + 1, conversation.Messages.Count);
        CollectionAssert.AreEqual(messagesBefore, conversation.Messages.Take(messagesBefore.Length).ToArray());
        var toolReply = conversation.Messages[^1];
        Assert.AreEqual("tool", toolReply.Role);
        Assert.AreEqual(advice.ToolCallId, toolReply.ToolCallId);
        Assert.AreEqual("REJECTED: User rejected this change. Do NOT retry the same edit. Ask the user what they want instead.",
            toolReply.Content);
        if (remaining != null)
        {
            Assert.AreEqual(AdviceStatus.Pending, remaining.Status);
            CollectionAssert.AreEqual(new[] { remaining.Id }, conversation.PendingAdviceIds);
        }
        else
        {
            Assert.AreEqual(0, conversation.PendingAdviceIds.Count);
        }
    }

    private static async Task AssertContinuationRejected(AgentConversation conversation, string userId)
    {
        var before = JsonSerializer.Serialize(conversation);
        var agent = new FakeAgentService(conversation);
        using var users = CreateUserManager();
        var controller = CreateController(agent, users, userId);

        var result = await controller.SendMessage(new SendMessageRequest
        {
            ConversationId = conversation.Id,
            Message = "Continue editing",
            FullDocumentContent = "Untrusted editor content"
        });

        Assert.IsInstanceOfType<BadRequestObjectResult>(result);
        Assert.AreEqual(before, JsonSerializer.Serialize(conversation));
        Assert.AreEqual(0, agent.ContinueCalls);
    }

    private static AgentConversation CreateConversation(string userId, AgentState state) => new()
    {
        UserId = userId,
        DocumentId = Guid.NewGuid(),
        DocumentContent = "Original document content",
        State = state,
        LastActivity = DateTime.UtcNow.AddMinutes(-5),
        Messages = [new ToolMessagesItem { Role = "user", Content = "Original message" }]
    };

    private static Advice CreateAdvice(AdviceService service, AgentConversation conversation)
    {
        var advice = service.Create(conversation.Id, "ReplaceText", "Replace text", "Edit document",
            new Dictionary<string, object?> { ["oldString"] = "Original", ["newString"] = "Edited" },
            "Original -> Edited", $"tool-{Guid.NewGuid()}",
            documentContentSnapshot: conversation.DocumentContent);
        conversation.PendingAdviceIds.Add(advice.Id);
        return advice;
    }

    private static AgentService CreateAgentService(AdviceService adviceService,
        params AgentConversation[] conversations)
    {
        // These synchronous paths must never use a model, scope or background queue.
        // Null dependencies fail immediately if an invalid approval reaches the queue.
        var service = new AgentService(null!, null!, adviceService, null!, null!, NullLogger<AgentService>.Instance);
        var field = typeof(AgentService).GetField("_conversations", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field);
        var stored = (ConcurrentDictionary<Guid, AgentConversation>)field.GetValue(service)!;
        foreach (var conversation in conversations)
        {
            stored[conversation.Id] = conversation;
        }
        return service;
    }

    private static void DecideAdvice(AgentService service, Guid conversationId, Guid adviceId, bool approve)
    {
        if (approve)
            service.ApproveAdvice(conversationId, adviceId);
        else
            service.RejectAdvice(conversationId, adviceId);
    }

    private static AgentController CreateController(IAgentService agent, UserManager<User> users, string userId)
    {
        // Continuation must not need a database or an advice service.
        return new AgentController(agent, null!, null!, users)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
                }
            }
        };
    }

    private static UserManager<User> CreateUserManager() => new(
        new UnusedUserStore(), Options.Create(new IdentityOptions()), new PasswordHasher<User>(),
        [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(), null!,
        NullLogger<UserManager<User>>.Instance);

    private sealed class UnusedUserStore : IUserStore<User>
    {
        public void Dispose() { }
        public Task<string> GetUserIdAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetUserNameAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetUserNameAsync(User user, string? userName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetNormalizedUserNameAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetNormalizedUserNameAsync(User user, string? normalizedName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IdentityResult> CreateAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IdentityResult> UpdateAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IdentityResult> DeleteAsync(User user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<User?> FindByIdAsync(string userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<User?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAgentService(AgentConversation? conversation) : IAgentService
    {
        public int ContinueCalls { get; private set; }
        public Guid? ContinuedId { get; private set; }
        public string? ContinuedUserId { get; private set; }
        public string? ContinuedMessage { get; private set; }
        public string? ContentWhenContinued { get; private set; }

        public AgentConversation? GetConversation(Guid conversationId) =>
            conversation?.Id == conversationId ? conversation : null;

        public Guid? ContinueRun(Guid conversationId, string userId, string userMessage)
        {
            ContinueCalls++;
            ContinuedId = conversationId;
            ContinuedUserId = userId;
            ContinuedMessage = userMessage;
            ContentWhenContinued = conversation?.DocumentContent;
            return conversationId;
        }

        public Task<Guid> StartRun(string userId, Guid documentId, string documentContent, string? documentTitle,
            string userMessage) => throw new NotSupportedException();
        public void ApproveAdvice(Guid conversationId, Guid adviceId) => throw new NotSupportedException();
        public void RejectAdvice(Guid conversationId, Guid adviceId) => throw new NotSupportedException();
        public void CancelRun(Guid conversationId) => throw new NotSupportedException();
        public string GetUpdatedDocumentContent(Guid conversationId) => throw new NotSupportedException();
    }
}
