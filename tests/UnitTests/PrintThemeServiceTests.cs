using Aiursoft.MarkToHtml.Services;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class PrintThemeServiceTests
{
    private class FakeEnv : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; } = null!;
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = string.Empty;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [TestMethod]
    public void TestPrintThemes_WithValidAndInvalidDirectories()
    {
        var temp = Path.Combine(Path.GetTempPath(), "PrintThemes_" + Guid.NewGuid());
        Directory.CreateDirectory(temp);

        try
        {
            var env = new FakeEnv { WebRootPath = temp };
            var service = new PrintThemeService(env);

            // Initially empty
            Assert.AreEqual(0, service.GetThemes().Count);
            Assert.AreEqual(0, service.GetThemeViewModels().Count);

            var root = Path.Combine(temp, "print-themes");
            Directory.CreateDirectory(root);

            // Invalid theme (missing css)
            var badDir = Path.Combine(root, "bad");
            Directory.CreateDirectory(badDir);
            File.WriteAllText(Path.Combine(badDir, "theme.json"), "{\"id\":\"bad\",\"name\":\"Bad\"}");

            // Valid theme
            var goodDir = Path.Combine(root, "good");
            Directory.CreateDirectory(goodDir);
            File.WriteAllText(Path.Combine(goodDir, "theme.json"), "{\"id\":\"good\",\"name\":\"Good Theme\",\"order\":1}");
            File.WriteAllText(Path.Combine(goodDir, "theme.css"), "body {}");

            var themes = service.GetThemes();
            Assert.AreEqual(1, themes.Count);
            Assert.AreEqual("good", themes[0].Id);
            Assert.AreEqual("Good Theme", themes[0].Name);

            var picked = service.PickTheme("good");
            Assert.AreEqual("good", picked.Id);

            var pickedFallback = service.PickTheme("non-existent");
            Assert.AreEqual("good", pickedFallback.Id);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }
        }
    }
}
