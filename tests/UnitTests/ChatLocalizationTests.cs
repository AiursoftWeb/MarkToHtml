using System.Xml.Linq;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class ChatLocalizationTests
{
    private static readonly string[] WindowSizeKeys =
    [
        "Chat window size",
        "Small",
        "Medium",
        "Large"
    ];

    [TestMethod]
    public void AllChatCulturesContainWindowSizeTranslations()
    {
        var repositoryRoot = FindRepositoryRoot();
        var resourceDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "Aiursoft.MarkToHtml",
            "Resources",
            "Views",
            "Agent");
        var resourceFiles = Directory.GetFiles(resourceDirectory, "Chat.*.resx");

        Assert.HasCount(27, resourceFiles);
        foreach (var resourceFile in resourceFiles)
        {
            var document = XDocument.Load(resourceFile);
            var resources = document.Root!
                .Elements("data")
                .ToDictionary(
                    element => element.Attribute("name")!.Value,
                    element => element.Element("value")?.Value);

            foreach (var key in WindowSizeKeys)
            {
                Assert.IsTrue(
                    resources.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value),
                    $"Resource '{resourceFile}' is missing a translation for '{key}'.");
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiursoft.MarkToHtml.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
