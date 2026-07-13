using System.Net;
using System.Net.Http.Headers;
using Aiursoft.MarkToHtml.Services.FileStorage;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class FilesTests : TestBase
{
    [TestMethod]
    public async Task UploadNoFileTest()
    {
        await LoginAsAdmin();
        var storage = GetService<StorageService>();
        var token = storage.GetToken("testfolder", FilePermission.Upload);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("dummy"), "dummy");
        var uploadResponse = await Http.PostAsync($"/upload/testfolder?token={Uri.EscapeDataString(token)}", form);
        Assert.AreEqual(HttpStatusCode.BadRequest, uploadResponse.StatusCode);
    }

    [TestMethod]
    public async Task UploadInvalidFileNameTest()
    {
        await LoginAsAdmin();
        var storage = GetService<StorageService>();
        var token = storage.GetToken("testfolder", FilePermission.Upload);

        var content = "Content";
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/plain");
        form.Add(fileContent, "file", "../../../etc/passwd"); // Invalid name

        var uploadResponse = await Http.PostAsync($"/upload/testfolder?token={Uri.EscapeDataString(token)}", form);
        Assert.AreEqual(HttpStatusCode.BadRequest, uploadResponse.StatusCode);
    }
}
