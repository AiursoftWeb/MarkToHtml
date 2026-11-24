using Aiursoft.MarkToHtml.Models.AgentViewModels;
using Aiursoft.MarkToHtml.Models.ErrorViewModels;
using Aiursoft.MarkToHtml.Models.PublicViewModels;
using Aiursoft.MarkToHtml.Services;
using Aiursoft.MarkToHtml.Services.Agent;
using Microsoft.Extensions.Caching.Memory;
using ModelContextProtocol.Protocol;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class SearchRateLimiterAndMiscTests
{
    [TestMethod]
    public void TestSearchRateLimiter()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var limiter = new SearchRateLimiter(cache);

        var ip = "127.0.0.1";
        for (var i = 0; i < 8; i++)
        {
            Assert.IsTrue(limiter.TryConsume(ip));
        }

        // 9th should be rate-limited
        Assert.IsFalse(limiter.TryConsume(ip));

        // Different IP should succeed
        Assert.IsTrue(limiter.TryConsume("192.168.1.1"));
    }

    [TestMethod]
    public async Task TestNullMcpServer()
    {
        var server = NullMcpServer.Instance;
        Assert.IsNull(server.ClientCapabilities);
        Assert.IsNull(server.ClientInfo);
        Assert.IsNotNull(server.ServerOptions);
        Assert.IsNull(server.Services);
#pragma warning disable CS0618
#pragma warning disable MCP9005
        Assert.IsNull(server.LoggingLevel);
#pragma warning restore MCP9005
#pragma warning restore CS0618
        Assert.IsNull(server.NegotiatedProtocolVersion);
        Assert.IsNull(server.SessionId);

        await server.RunAsync();
        await server.SendMessageAsync(new JsonRpcNotification { Method = "notify" });
        var response = await server.SendRequestAsync(new JsonRpcRequest { Id = new RequestId(1), Method = "req" });
        Assert.IsNotNull(response);

        var disposable = server.RegisterNotificationHandler("test", (_, _) => ValueTask.CompletedTask);
        await disposable.DisposeAsync();
        await server.DisposeAsync();
    }

    [TestMethod]
    public void TestViewModels()
    {
        var unauth = new UnauthorizedViewModel
        {
            ReturnUrl = "/test"
        };
        Assert.AreEqual("/test", unauth.ReturnUrl);

        var adviceId = Guid.NewGuid();
        var advice = new AdviceViewModel
        {
            AdviceId = adviceId,
            ToolDisplayName = "Replace",
            ParameterDisplay = "a -> b",
            Status = "pending",
            OldString = "old",
            NewString = "new",
            Parameters = [new ParameterItemViewModel { Key = "k", DisplayKey = "K", Value = "v" }]
        };
        Assert.AreEqual(adviceId, advice.AdviceId);
        Assert.AreEqual("Replace", advice.ToolDisplayName);
        Assert.AreEqual(1, advice.Parameters.Count);

        var convId = Guid.NewGuid();
        var status = new AgentStatusViewModel
        {
            ConversationId = convId,
            State = "idle",
            ErrorMessage = null,
            UpdatedDocumentContent = "doc"
        };
        Assert.AreEqual(convId, status.ConversationId);
        Assert.AreEqual("idle", status.State);

        var chatMsg = new ChatMessageViewModel
        {
            Role = "user",
            Content = "hello",
            IsMeta = false,
            ToolCallId = "call-1",
            ToolCalls = [new ToolCallViewModel { Id = "call-1", Name = "ReplaceText", Arguments = "{}" }]
        };
        Assert.AreEqual("user", chatMsg.Role);
        Assert.AreEqual("hello", chatMsg.Content);
        Assert.AreEqual(1, chatMsg.ToolCalls.Count);

        var diff = new DiffHunkViewModel
        {
            OldStart = 1,
            NewStart = 1,
            Lines = ["+ line1", "- line2"]
        };
        Assert.AreEqual(1, diff.OldStart);
        Assert.AreEqual(2, diff.Lines.Count);

        var pubDoc = new PublicDocumentViewModel("Title")
        {
            DocumentTitle = "Doc Title",
            Content = "<p>content</p>",
            MarkdownContent = "content",
            AuthorName = "author",
            CreationTime = DateTime.UtcNow,
            CanEdit = true
        };
        Assert.AreEqual("Doc Title", pubDoc.DocumentTitle);
        Assert.IsTrue(pubDoc.CanEdit);
    }
}
