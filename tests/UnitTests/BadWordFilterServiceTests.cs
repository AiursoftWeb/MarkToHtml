using Aiursoft.MarkToHtml.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class BadWordFilterServiceTests
{
    private class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Aiursoft.MarkToHtml";
    }

    private readonly BadWordFilterService _service;

    public BadWordFilterServiceTests()
    {
        var env = new FakeWebHostEnvironment();
        // Create a dummy badwords.txt for testing if it doesn't exist
        var path = Path.Combine(env.ContentRootPath, "badwords.txt");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "法轮\n六四\nFREEGATE");
        }
        _service = new BadWordFilterService(env);
    }

    [TestMethod]
    [DataRow("This is a clean text.")]
    [DataRow("Hello World!")]
    [DataRow(null)]
    [DataRow("")]
    public void ContainsBadWord_CleanText_ReturnsFalse(string text)
    {
        var result = _service.ContainsBadWord(text);
        Assert.IsFalse(result);
    }

    [TestMethod]
    [DataRow("Some content with 法轮 in it.")]
    [DataRow("Text containing 六四.")]
    [DataRow("Mixed case FREEGATE inside.")]
    public void ContainsBadWord_BadText_ReturnsTrue(string text)
    {
        var result = _service.ContainsBadWord(text);
        Assert.IsTrue(result);
    }
}
