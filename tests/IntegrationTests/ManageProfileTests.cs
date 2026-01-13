using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.MarkToHtml.Entities;
using Microsoft.Extensions.DependencyInjection;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class ManageProfileTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public ManageProfileTests()
    {
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };
        _port = Network.GetAvailablePort();
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://localhost:{_port}")
        };
    }

    [TestInitialize]
    public async Task CreateServer()
    {
        _server = await AppAsync<Startup>([], port: _port);
        await _server.UpdateDbAsync<TemplateDbContext>();
        await _server.SeedAsync();
        await _server.StartAsync();
    }

    [TestCleanup]
    public async Task CleanServer()
    {
        if (_server == null) return;
        await _server.StopAsync();
        _server.Dispose();
    }

    private async Task<string> GetAntiCsrfToken(string url)
    {
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html,
            @"<input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)"" />");
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not find anti-CSRF token on page: {url}");
        }

        return match.Groups[1].Value;
    }

    private async Task<(string email, string password)> RegisterAndLoginAsync()
    {
        var email = $"user-{Guid.NewGuid()}@aiursoft.com";
        var password = "Test-Password-123";

        var registerToken = await GetAntiCsrfToken("/Account/Register");
        var registerContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password },
            { "__RequestVerificationToken", registerToken }
        });
        var registerResponse = await _http.PostAsync("/Account/Register", registerContent);
        Assert.AreEqual(HttpStatusCode.Found, registerResponse.StatusCode);

        return (email, password);
    }
    
    private async Task<string> UploadFileAsync(byte[] content, string fileName, string contentType)
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);

        var uploadResponse = await _http.PostAsync("/upload/avatar", form);
        uploadResponse.EnsureSuccessStatusCode();

        var json = await uploadResponse.Content.ReadAsStringAsync();
        var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
        
        return jsonDoc.RootElement.GetProperty("path").GetString()!;
    }

    [TestMethod]
    public async Task ChangeAvatarTest()
    {
        // 1. Register and Login
        await RegisterAndLoginAsync();

        // 2. Upload a valid image (1x1 PNG transparent)
        // 1x1 PNG Bytes
        var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        var imagePath = await UploadFileAsync(pngBytes, "avatar.png", "image/png");

        // 3. Change Avatar to this image
        var changeAvatarToken = await GetAntiCsrfToken("/Manage/ChangeAvatar");
        var changeAvatarContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "AvatarUrl", imagePath },
            { "__RequestVerificationToken", changeAvatarToken }
        });

        var response = await _http.PostAsync("/Manage/ChangeAvatar", changeAvatarContent);
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.AreEqual("/Manage?Message=ChangeAvatarSuccess", response.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task ChangeAvatarInvalidImageTest()
    {
        // 1. Register and Login
        await RegisterAndLoginAsync();

        // 2. Upload a text file
        var textBytes = System.Text.Encoding.UTF8.GetBytes("This is not an image.");
        var filePath = await UploadFileAsync(textBytes, "not-image.txt", "text/plain");

        // 3. Try to set as Avatar
        var changeAvatarToken = await GetAntiCsrfToken("/Manage/ChangeAvatar");
        var changeAvatarContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "AvatarUrl", filePath },
            { "__RequestVerificationToken", changeAvatarToken }
        });

        var response = await _http.PostAsync("/Manage/ChangeAvatar", changeAvatarContent);
        
        // Should return OK (view with error) instead of redirect
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("The file is not a valid image") || html.Contains("error"), "Should show error message");
    }
}
