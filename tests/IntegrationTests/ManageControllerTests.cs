using Aiursoft.MarkToHtml.Services;

using Aiursoft.MarkToHtml.Entities;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class ManageControllerTests : TestBase
{
    [TestMethod]
    public async Task TestManageWorkflow()
    {
        await LoginAsAdmin();

        // Ensure AllowUserAdjustNickname is true
        using (var scope = Server!.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            await settingsService.UpdateSettingAsync(Configuration.SettingsMap.AllowUserAdjustNickname, "True");
        }

        // 1. Index
        var indexResponse = await Http.GetAsync("/Manage/Index");
        indexResponse.EnsureSuccessStatusCode();

        // 2. ChangePassword (GET)
        var changePasswordPage = await Http.GetAsync("/Manage/ChangePassword");
        changePasswordPage.EnsureSuccessStatusCode();

        // 3. ChangeProfile (GET)
        var changeProfilePage = await Http.GetAsync("/Manage/ChangeProfile");
        changeProfilePage.EnsureSuccessStatusCode();

        // 4. ChangeAvatar (GET)
        var changeAvatarPage = await Http.GetAsync("/Manage/ChangeAvatar");
        changeAvatarPage.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task TestChangeAvatarAllowsJpegExtension()
    {
        await LoginAsAdmin();

        var response = await Http.GetAsync("/Manage/ChangeAvatar");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-allowed-file-extensions=\"png bmp jpg jpeg\"", html);
        Assert.Contains("validExtensions: ('png bmp jpg jpeg' || '').split(' ').filter(Boolean)", html);
    }

    [TestMethod]
    public async Task TestDeleteAccount_WithAssets_Blocked()
    {
        // Arrange: register, login, and create an asset owned by the user
        var (email, _) = await RegisterAndLoginAsync();

        string userId;
        using (var scope = Server!.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByEmailAsync(email);
            userId = user!.Id;

            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.MarkdownDocuments.Add(new MarkdownDocument { UserId = userId });
            await db.SaveChangesAsync();
        }

        // Act: try to delete account
        var deleteResponse = await PostForm("/Manage/DeleteAccountPost", new(), tokenUrl: "/Manage/ChangePassword");

        // Assert: blocked — redirected back to confirmation page, NOT "/"
        AssertRedirect(deleteResponse, "/Manage/DeleteAccount");

        // Assert: user still exists
        using (var scope = Server!.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Assert.IsNotNull(await userManager.FindByEmailAsync(email));
        }

        // Assert: asset still exists
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            Assert.IsTrue(await db.MarkdownDocuments.AnyAsync(d => d.UserId == userId));
        }
    }

    [TestMethod]
    public async Task TestDeleteAccount_NoAssets_Succeeds()
    {
        // Arrange: register and login (no assets created)
        var (email, _) = await RegisterAndLoginAsync();

        // Act: confirmation page loads
        var deletePage = await Http.GetAsync("/Manage/DeleteAccount");
        deletePage.EnsureSuccessStatusCode();

        // Act: confirm deletion
        var deleteResponse = await PostForm("/Manage/DeleteAccountPost", new(), tokenUrl: "/Manage/ChangePassword");
        AssertRedirect(deleteResponse, "/");

        // Assert: signed out
        var managePage = await Http.GetAsync("/Manage/Index");
        Assert.AreEqual(HttpStatusCode.Found, managePage.StatusCode);

        // Assert: user gone from DB
        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        Assert.IsNull(await userManager.FindByEmailAsync(email));
    }

    [TestMethod]
    public async Task TestDeleteAccount_Unauthenticated_RedirectsToLogin()
    {
        var deletePage = await Http.GetAsync("/Manage/DeleteAccount");
        Assert.AreEqual(HttpStatusCode.Found, deletePage.StatusCode);
    }
}
