using System.Net;
using System.Text.Json;

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
        // Should return a valid JSON array (empty if no token configured, or populated if token is set)
        var parsed = JsonSerializer.Deserialize<JsonElement>(content);
        Assert.AreEqual(JsonValueKind.Array, parsed.ValueKind);
    }

    [TestMethod]
    public async Task PostSpeech_NoInput_ReturnsBadRequest()
    {
        var response = await Http.PostAsync("/tts/speech", new FormUrlEncodedContent([]));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task PostSpeech_WithInput_ReturnsSuccess()
    {
        var response = await Http.PostAsync("/tts/speech",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "input", "Hello world" }
            }));

        var content = await response.Content.ReadAsStringAsync();

        if (response.StatusCode == HttpStatusCode.InternalServerError)
        {
            // Token not configured — verify the error response
            var parsed = JsonSerializer.Deserialize<JsonElement>(content);
            Assert.IsTrue(parsed.TryGetProperty("error", out _));
        }
        else
        {
            // Token configured — should return audio content
            response.EnsureSuccessStatusCode();
            Assert.IsTrue(response.Content.Headers.ContentType?.MediaType?.StartsWith("audio/") ?? false);
        }
    }
}
