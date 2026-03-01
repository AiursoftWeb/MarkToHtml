using System.Net;
using System.Net.Http.Headers;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Services.FileStorage;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class FilesTests
{
    private int _port;
    private HttpClient _http = null!;
    private IHost? _server;

    [TestInitialize]
    public async Task CreateServer()
    {
        _port = Network.GetAvailablePort();
        _http = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{_port}")
        };

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

    [TestMethod]
    public async Task UploadNoFileTest()
    {
        var storage = _server!.Services.GetRequiredService<StorageService>();
        var token = storage.GetToken("testfolder", FilePermission.Upload);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("dummy"), "dummy");
        var uploadResponse = await _http.PostAsync($"/upload/testfolder?token={Uri.EscapeDataString(token)}", form);
        Assert.AreEqual(HttpStatusCode.BadRequest, uploadResponse.StatusCode);
    }

    [TestMethod]
    public async Task UploadInvalidFileNameTest()
    {
        var storage = _server!.Services.GetRequiredService<StorageService>();
        var token = storage.GetToken("testfolder", FilePermission.Upload);

        var content = "Content";
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        form.Add(fileContent, "file", "../../../etc/passwd"); // Invalid name

        var uploadResponse = await _http.PostAsync($"/upload/testfolder?token={Uri.EscapeDataString(token)}", form);
        Assert.AreEqual(HttpStatusCode.BadRequest, uploadResponse.StatusCode);
    }
}
