using System.Net;
using Aiursoft.MarkToHtml.Services.FileStorage;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class MarkToHtmlUploadPolicyTests : TestBase
{
    [TestMethod]
    public async Task AnonymousUploadIsRejected()
    {
        var storage = GetService<StorageService>();
        var request = new MultipartFormDataContent
        {
            { new StringContent("content"), "file", "document.txt" }
        };

        var response = await Http.PostAsync(storage.GetUploadUrl("documents"), request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task UploadedFileNamesUseUrlSafeSpaces()
    {
        await LoginAsAdmin();
        var storage = GetService<StorageService>();
        var request = new MultipartFormDataContent
        {
            { new StringContent("content"), "file", "my document.txt" }
        };

        var response = await Http.PostAsync(storage.GetUploadUrl("documents"), request);
        response.EnsureSuccessStatusCode();
        var upload = await response.Content.ReadFromJsonAsync<UploadResult>();

        Assert.AreEqual("documents/my-document.txt", upload?.Path);
    }

    [TestMethod]
    public async Task ArticleSvgIsRenderedWithARestrictiveSandbox()
    {
        await LoginAsAdmin();
        var storage = GetService<StorageService>();
        var request = new MultipartFormDataContent
        {
            {
                new StringContent(
                    "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(document.domain)</script><rect width=\"10\" height=\"10\"/></svg>"),
                "file",
                "diagram.svg"
            }
        };

        var uploadResponse = await Http.PostAsync(storage.GetUploadUrl("markdown-images"), request);
        uploadResponse.EnsureSuccessStatusCode();
        var upload = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>();

        var downloadResponse = await Http.GetAsync($"/download/{upload!.Path}");

        downloadResponse.EnsureSuccessStatusCode();
        Assert.AreEqual("image/svg+xml", downloadResponse.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("inline", downloadResponse.Content.Headers.ContentDisposition?.DispositionType);
        Assert.AreEqual("nosniff", downloadResponse.Headers.GetValues("X-Content-Type-Options").Single());
        var csp = downloadResponse.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("sandbox", csp);
        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("style-src 'unsafe-inline'", csp);
    }

    [TestMethod]
    public async Task SvgOutsideArticleImagesIsDownloaded()
    {
        await LoginAsAdmin();
        var storage = GetService<StorageService>();
        var request = new MultipartFormDataContent
        {
            {
                new StringContent("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>"),
                "file",
                "image.svg"
            }
        };

        var uploadResponse = await Http.PostAsync(storage.GetUploadUrl("documents"), request);
        uploadResponse.EnsureSuccessStatusCode();
        var upload = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>();

        var downloadResponse = await Http.GetAsync($"/download/{upload!.Path}");

        downloadResponse.EnsureSuccessStatusCode();
        Assert.AreEqual("application/octet-stream", downloadResponse.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual("attachment", downloadResponse.Content.Headers.ContentDisposition?.DispositionType);
    }

    private sealed class UploadResult
    {
        public string Path { get; init; } = string.Empty;
    }
}
