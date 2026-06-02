using System.Net;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class TtsControllerTests : TestBase
{
    [TestMethod]
    public async Task GetVoices_ReturnsJsonArray()
    {
        var response = await Http.GetAsync("/tts/voices");
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        // Should return an empty JSON array when ApiToken is not configured
        Assert.AreEqual("[]", content);
    }

    [TestMethod]
    public async Task PostSpeech_NoInput_ReturnsBadRequest()
    {
        var response = await Http.PostAsync("/tts/speech", new FormUrlEncodedContent([]));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PostSpeech_NoToken_ReturnsError()
    {
        var response = await Http.PostAsync("/tts/speech",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "input", "Hello world" }
            }));
        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
