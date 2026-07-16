using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Services.BackgroundJobs;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class OrphanAvatarCleanupJobTests : TestBase
{
    private string AvatarDir => Path.Combine(StoragePath, "Workspace", "avatar");

    private string CreateAvatarFile(string filename, bool isOld)
    {
        Directory.CreateDirectory(AvatarDir);
        var path = Path.Combine(AvatarDir, filename);
        File.WriteAllText(path, "fake-avatar-data");
        if (isOld)
        {
            var oldTime = DateTime.UtcNow.AddHours(-8);
            File.SetLastWriteTimeUtc(path, oldTime);
        }
        return path;
    }

    private async Task RunJob()
    {
        var job = Server!.Services.GetRequiredService<OrphanAvatarCleanupJob>();
        await job.ExecuteAsync();
    }

    [TestMethod]
    public async Task OldOrphanAvatarIsDeleted()
    {
        var orphanPath = CreateAvatarFile("orphan-old.png", isOld: true);
        await RunJob();
        Assert.IsFalse(File.Exists(orphanPath), "An old orphan avatar should have been deleted by the cleanup job.");
    }

    [TestMethod]
    public async Task NewOrphanAvatarIsKeptWithinGracePeriod()
    {
        var freshPath = CreateAvatarFile("orphan-fresh.png", isOld: false);
        await RunJob();
        Assert.IsTrue(File.Exists(freshPath), "A freshly uploaded orphan avatar must be kept within the grace period to prevent race conditions.");
    }

    [TestMethod]
    public async Task ReferencedAvatarIsNeverDeleted()
    {
        var filename = "referenced-old.png";
        var referencedPath = CreateAvatarFile(filename, isOld: true);

        var db = Server!.Services.GetRequiredService<TemplateDbContext>();
        var admin = await db.Users.FirstAsync();
        admin.AvatarRelativePath = $"avatar/{filename}";
        db.Users.Update(admin);
        await db.SaveChangesAsync();

        await RunJob();

        Assert.IsTrue(File.Exists(referencedPath), "An avatar referenced by a user must never be deleted, even if it is old.");
    }
}
