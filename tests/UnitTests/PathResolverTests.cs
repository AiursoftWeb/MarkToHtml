using Aiursoft.MarkToHtml.Services.FileStorage;

namespace Aiursoft.MarkToHtml.Tests.UnitTests;

[TestClass]
public class PathResolverTests
{
    private string _testDir = null!;

    [TestInitialize]
    public void Init()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "PathResolverTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    [TestMethod]
    public void TestPathResolver()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:Path"] = _testDir
            })
            .Build();

        var resolver = new PathResolver(config);

        var workspaceFile = Path.Combine(_testDir, "Workspace", "sub", "test.png");
        Directory.CreateDirectory(Path.GetDirectoryName(workspaceFile)!);
        File.WriteAllText(workspaceFile, "fake content");

        var cleared = resolver.GetClearedExifAbsolutePath(workspaceFile);
        Assert.IsTrue(cleared.Contains("ClearExif"));
        Assert.IsTrue(cleared.EndsWith("test.png"));

        var compressedBoth = resolver.GetCompressedAbsolutePath(workspaceFile, 100, 200);
        Assert.IsTrue(compressedBoth.Contains("Compressed"));
        Assert.IsTrue(compressedBoth.Contains("_w100_h200.png"));

        var compressedW = resolver.GetCompressedAbsolutePath(workspaceFile, 100, 0);
        Assert.IsTrue(compressedW.Contains("_w100.png"));

        var compressedH = resolver.GetCompressedAbsolutePath(workspaceFile, 0, 200);
        Assert.IsTrue(compressedH.Contains("_h200.png"));

        var compressedNone = resolver.GetCompressedAbsolutePath(workspaceFile, 0, 0);
        Assert.IsTrue(compressedNone.EndsWith("test.png"));
    }

    [TestMethod]
    public void TestMissingConfigThrows()
    {
        var config = new ConfigurationBuilder().Build();
        try
        {
            _ = new PathResolver(config);
            Assert.Fail("Should have thrown InvalidDataException");
        }
        catch (InvalidDataException)
        {
            // Expected
        }
    }
}
