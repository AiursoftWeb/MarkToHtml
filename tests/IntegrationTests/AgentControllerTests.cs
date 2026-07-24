using System.Net;
using Aiursoft.MarkToHtml.Models.AgentViewModels;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class AgentControllerTests : TestBase
{
    private async Task<string> GetCsrfToken(string url = "/")
    {
        var response = await Http.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        var match = System.Text.RegularExpressions.Regex.Match(body,
            @"<input[^>]*name=""__RequestVerificationToken""[^>]*value=""([^""]+)""");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private Task<HttpResponseMessage> PostJsonWithResponseAsync(string url, object data)
    {
        return GetCsrfToken().ContinueWith(async t =>
        {
            var token = t.Result;
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("RequestVerificationToken", token);
            request.Content = JsonContent.Create(data);
            return await Http.SendAsync(request);
        }).Unwrap();
    }

    [TestMethod]
    public async Task TestAnonymousAccessRejected()
    {
        // Anonymous user should NOT be able to access agent endpoints
        var response = await PostJsonWithResponseAsync("/Agent/SendMessage", new SendMessageRequest
        {
            DocumentId = Guid.NewGuid(),
            Message = "Hello"
        });
        // Should redirect to login (302) or return 401
        Assert.IsTrue(
            response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized,
            $"Expected redirect/unauthorized but got {response.StatusCode}");
    }

    [TestMethod]
    public async Task TestSendMessageRequiresDocument()
    {
        await RegisterAndLoginAsync();

        var response = await PostJsonWithResponseAsync("/Agent/SendMessage", new SendMessageRequest
        {
            DocumentId = Guid.Empty,
            Message = "Hello"
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "Document ID is required");
    }

    [TestMethod]
    public async Task TestSendMessageDocumentNotFound()
    {
        await RegisterAndLoginAsync();

        var response = await PostJsonWithResponseAsync("/Agent/SendMessage", new SendMessageRequest
        {
            DocumentId = Guid.NewGuid(),
            Message = "Test change request"
        });
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestStatusConversationNotFound()
    {
        await RegisterAndLoginAsync();

        var response = await Http.GetAsync($"/Agent/Status?conversationId={Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestCancelConversationNotFound()
    {
        await RegisterAndLoginAsync();

        var token = await GetCsrfToken();
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/Agent/Cancel?conversationId={Guid.NewGuid()}");
        request.Headers.Add("RequestVerificationToken", token);
        var response = await Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestApproveAdviceConversationNotFound()
    {
        await RegisterAndLoginAsync();

        var token = await GetCsrfToken();
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/Agent/ApproveAdvice?conversationId={Guid.NewGuid()}&adviceId={Guid.NewGuid()}");
        request.Headers.Add("RequestVerificationToken", token);
        var response = await Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestEditPageRendersChatWidget()
    {
        await RegisterAndLoginAsync();

        // Create a document via SaveNew
        var saveResponse = await PostForm("/Home/SaveNew", new Dictionary<string, string>
        {
            { "DocumentId", Guid.NewGuid().ToString() },
            { "Title", "Test Chat Widget" },
            { "InputMarkdown", "# Hello World" }
        });
        Assert.AreEqual(HttpStatusCode.Found, saveResponse.StatusCode);

        // Follow redirect to edit page
        var editLocation = saveResponse.Headers.Location?.OriginalString ?? "";
        Assert.IsTrue(editLocation.Contains("/Home/Edit/"),
            $"Expected redirect to Edit page, got {editLocation}");

        var editResponse = await Http.GetAsync(editLocation);
        editResponse.EnsureSuccessStatusCode();
        var html = await editResponse.Content.ReadAsStringAsync();

        // Verify chat widget elements exist in the HTML
        Assert.IsTrue(html.Contains("agent-chat-widget"),
            "Chat widget container should be in the edit page HTML");
        Assert.IsTrue(html.Contains("agent-send-btn"),
            "Send button should be in the edit page HTML");
        Assert.IsTrue(html.Contains("agent-chat.js"),
            "agent-chat.js script reference should be in the page");
    }

    [TestMethod]
    public async Task TestRejectAdviceConversationNotFound()
    {
        await RegisterAndLoginAsync();

        var token = await GetCsrfToken();
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"/Agent/RejectAdvice?conversationId={Guid.NewGuid()}&adviceId={Guid.NewGuid()}");
        request.Headers.Add("RequestVerificationToken", token);
        var response = await Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
