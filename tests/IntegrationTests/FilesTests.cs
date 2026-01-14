using System.Net;
using System.Net.Http.Headers;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.MarkToHtml.Entities;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class FilesTests
{
    private readonly int _port;
    private readonly HttpClient _http;
    private IHost? _server;

    public FilesTests()
    {
        _port = Network.GetAvailablePort();
        _http = new HttpClient
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

    [TestMethod]
    public async Task UploadNoFileTest()
    {
        using var form = new MultipartFormDataContent();
        var uploadResponse = await _http.PostAsync("/upload/testfolder", form);
        Assert.AreEqual(HttpStatusCode.BadRequest, uploadResponse.StatusCode);
    }

    [TestMethod]
    public async Task UploadInvalidFileNameTest()
    {
        var content = "Content";
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        form.Add(fileContent, "file", "../../../etc/passwd"); // Invalid name

        var uploadResponse = await _http.PostAsync("/upload/testfolder", form);
        Assert.AreEqual(HttpStatusCode.BadRequest, uploadResponse.StatusCode);
    }
}
