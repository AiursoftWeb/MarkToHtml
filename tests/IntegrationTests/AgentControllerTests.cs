using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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

    private async Task PostJsonAsync(string url, object data)
    {
        var token = await GetCsrfToken();
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("RequestVerificationToken", token);
        request.Content = JsonContent.Create(data);
        await Http.SendAsync(request);
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
