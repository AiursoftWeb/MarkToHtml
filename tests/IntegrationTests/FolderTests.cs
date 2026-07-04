using System.Net;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Sqlite;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.Tests.IntegrationTests;

[TestClass]
public class FolderTests : TestBase
{
    [TestMethod]
    public async Task GetHistory_AtRoot_ShowsRootFoldersAndDocuments()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        db.MarkdownDocumentFolders.Add(new MarkdownDocumentFolder { Name = "Work", UserId = user.Id });
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(), Title = "Root Note", Content = "Some content", UserId = user.Id
        });
        await db.SaveChangesAsync();

        var response = await Http.GetAsync("/Home/History");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Work"), "Root folder 'Work' should appear");
        Assert.IsTrue(html.Contains("Root Note"), "Root document 'Root Note' should appear");
    }

    [TestMethod]
    public async Task GetHistory_SupportsFolderBrowsing()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var folder = new MarkdownDocumentFolder { Name = "Projects", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(folder);
        await db.SaveChangesAsync();

        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(), Title = "In Folder", Content = "content", UserId = user.Id, FolderId = folder.Id
        });
        await db.SaveChangesAsync();

        var response = await Http.GetAsync($"/Home/History?folderId={folder.Id}");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("In Folder"), "Document inside folder should appear");
    }

    [TestMethod]
    public async Task CreateFolder_AtRoot_Success()
    {
        await RegisterAndLoginAsync();

        var getResponse = await Http.GetAsync("/Folder/CreateFolder");
        var getHtml = await getResponse.Content.ReadAsStringAsync();
        var token = ExtractAntiForgeryToken(getHtml);

        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token }, { "Name", "My New Folder" }
        });

        var response = await Http.PostAsync("/Folder/CreateFolder", formData);
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateFolder_AsChild_Success()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var parentFolder = new MarkdownDocumentFolder { Name = "Parent", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(parentFolder);
        await db.SaveChangesAsync();

        var getResponse = await Http.GetAsync($"/Folder/CreateFolder?id={parentFolder.Id}");
        var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());

        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Name", "Child Folder" },
            { "ParentFolderId", parentFolder.Id.ToString() }
        });

        var response = await Http.PostAsync("/Folder/CreateFolder", formData);
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);

        // Verify child inside parent
        var child = await db.MarkdownDocumentFolders
            .FirstOrDefaultAsync(f => f.Name == "Child Folder" && f.ParentFolderId == parentFolder.Id);
        Assert.IsNotNull(child);
    }

    [TestMethod]
    public async Task CreateFolder_DuplicateName_BlockedByValidation()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        db.MarkdownDocumentFolders.Add(new MarkdownDocumentFolder { Name = "Duplicate", UserId = user.Id });
        await db.SaveChangesAsync();

        var getResponse = await Http.GetAsync("/Folder/CreateFolder");
        var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());

        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token }, { "Name", "Duplicate" }
        });

        var response = await Http.PostAsync("/Folder/CreateFolder", formData);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "Duplicate name should return validation error");
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("already exists"), "Should show 'already exists' error");

        var folderCount = await db.MarkdownDocumentFolders
            .Where(f => f.Name == "Duplicate" && f.UserId == user.Id).CountAsync();
        Assert.AreEqual(1, folderCount, "Only one folder should exist");
    }

    [TestMethod]
    public async Task CreateFolder_DuplicateRootName_BlockedByDatabaseConstraint()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new SqliteContext(options);
        await db.Database.EnsureCreatedAsync();

        var user = new User
        {
            Id = "duplicate-root-folder-user",
            UserName = "duplicate-root-folder-user@test.com",
            Email = "duplicate-root-folder-user@test.com",
            DisplayName = "Duplicate Root Folder User"
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.MarkdownDocumentFolders.AddRange(
            new MarkdownDocumentFolder { Name = "Root Duplicate", UserId = user.Id },
            new MarkdownDocumentFolder { Name = "Root Duplicate", UserId = user.Id });

        var duplicateRejected = false;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            duplicateRejected = true;
        }

        Assert.IsTrue(duplicateRejected, "Database should reject duplicate root folder names for the same user.");
    }

    [TestMethod]
    public async Task EditFolder_RenameToExistingName_Blocked()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        db.MarkdownDocumentFolders.Add(new MarkdownDocumentFolder { Name = "Existing", UserId = user.Id });
        var folder = new MarkdownDocumentFolder { Name = "Target", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(folder);
        await db.SaveChangesAsync();

        var getResponse = await Http.GetAsync($"/Folder/EditFolder/{folder.Id}");
        var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());
        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Id", folder.Id.ToString() }, { "Name", "Existing" }
        });
        var response = await Http.PostAsync("/Folder/EditFolder", formData);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("already exists"));

        var untouched = await db.MarkdownDocumentFolders.FindAsync(folder.Id);
        Assert.AreEqual("Target", untouched!.Name);
    }

    [TestMethod]
    public async Task EditFolder_Rename_Success()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var folder = new MarkdownDocumentFolder { Name = "Old Name", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(folder);
        await db.SaveChangesAsync();

        var getResponse = await Http.GetAsync($"/Folder/EditFolder/{folder.Id}");
        var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());

        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Id", folder.Id.ToString() }, { "Name", "New Name" }
        });

        var response = await Http.PostAsync("/Folder/EditFolder", formData);
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);

        using var verifyScope = Server!.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var updated = await verifyDb.MarkdownDocumentFolders.FindAsync(folder.Id);
        Assert.IsNotNull(updated);
        Assert.AreEqual("New Name", updated.Name);
    }

    [TestMethod]
    public async Task EditFolder_MoveToRoot_Success()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var parentFolder = new MarkdownDocumentFolder { Name = "Parent", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(parentFolder);
        await db.SaveChangesAsync();

        var childFolder = new MarkdownDocumentFolder { Name = "Child", UserId = user.Id, ParentFolderId = parentFolder.Id };
        db.MarkdownDocumentFolders.Add(childFolder);
        await db.SaveChangesAsync();

        var getResponse = await Http.GetAsync($"/Folder/EditFolder/{childFolder.Id}");
        var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());

        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Id", childFolder.Id.ToString() }, { "Name", "Child" }, { "BrowseParentFolderId", "" }
        });

        var response = await Http.PostAsync("/Folder/EditFolder", formData);
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);

        using var verifyScope = Server!.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var updated = await verifyDb.MarkdownDocumentFolders.FindAsync(childFolder.Id);
        Assert.IsNotNull(updated);
        Assert.IsNull(updated.ParentFolderId);
    }

    [TestMethod]
    public async Task EditFolder_CircularReference_Blocked()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var parentFolder = new MarkdownDocumentFolder { Name = "Parent", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(parentFolder);
        await db.SaveChangesAsync();

        var childFolder = new MarkdownDocumentFolder { Name = "Child", UserId = user.Id, ParentFolderId = parentFolder.Id };
        db.MarkdownDocumentFolders.Add(childFolder);
        await db.SaveChangesAsync();

        var getResponse = await Http.GetAsync($"/Folder/EditFolder/{parentFolder.Id}?browseFolderId={childFolder.Id}");
        var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());

        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Id", parentFolder.Id.ToString() }, { "Name", "Parent" },
            { "BrowseParentFolderId", childFolder.Id.ToString() }
        });

        var response = await Http.PostAsync("/Folder/EditFolder", formData);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("own child"), "Circular reference error should be displayed");

        using var verifyScope = Server!.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var parent = await verifyDb.MarkdownDocumentFolders.FindAsync(parentFolder.Id);
        Assert.IsNotNull(parent);
        Assert.IsNull(parent.ParentFolderId);
    }

    [TestMethod]
    public async Task DeleteFolder_Empty_Success()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var folder = new MarkdownDocumentFolder { Name = "Empty Folder", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(folder);
        await db.SaveChangesAsync();

        // GET confirmation page
        var getResponse = await Http.GetAsync($"/Folder/DeleteFolder/{folder.Id}");
        getResponse.EnsureSuccessStatusCode();
        var html = await getResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Empty Folder"), "Confirmation page should show folder name");
        Assert.IsTrue(html.Contains("empty"), "Should indicate folder is empty");

        // POST to actually delete
        var token = ExtractAntiForgeryToken(html);
        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token }
        });

        var response = await Http.PostAsync($"/Folder/DeleteFolder/{folder.Id}", formData);
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);

        using var verifyScope = Server!.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var deleted = await verifyDb.MarkdownDocumentFolders.FindAsync(folder.Id);
        Assert.IsNull(deleted);
    }

    [TestMethod]
    public async Task DeleteFolder_WithContents_ShowsWarningAndDeletes()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var folder = new MarkdownDocumentFolder { Name = "Folder With Docs", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(folder);
        await db.SaveChangesAsync();

        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(), Title = "Doc in folder", Content = "content", UserId = user.Id, FolderId = folder.Id
        });
        await db.SaveChangesAsync();

        // GET confirmation page — should show document count
        var getResponse = await Http.GetAsync($"/Folder/DeleteFolder/{folder.Id}");
        getResponse.EnsureSuccessStatusCode();
        var html = await getResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Folder With Docs"), "Should show folder name");
        Assert.IsTrue(html.Contains("irreversible") || html.Contains("permanently"),
            "Should warn about irreversibility");
        Assert.IsTrue(html.Contains("1 document"), "Should show document count");

        // POST to cascade delete
        var token = ExtractAntiForgeryToken(html);
        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token }
        });

        var response = await Http.PostAsync($"/Folder/DeleteFolder/{folder.Id}", formData);
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);

        using var verifyScope = Server!.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsNull(await verifyDb.MarkdownDocumentFolders.FindAsync(folder.Id));
        Assert.IsFalse(await verifyDb.MarkdownDocuments.AnyAsync(d => d.FolderId == folder.Id),
            "Documents in folder should also be deleted");
    }

    [TestMethod]
    public async Task DeleteFolder_Recursive_ShowsNestedCounts()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var root = new MarkdownDocumentFolder { Name = "RootFolder", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(root);
        await db.SaveChangesAsync();

        var child = new MarkdownDocumentFolder { Name = "Child", UserId = user.Id, ParentFolderId = root.Id };
        db.MarkdownDocumentFolders.Add(child);
        await db.SaveChangesAsync();

        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(), Title = "Deep Doc", Content = "nested", UserId = user.Id, FolderId = child.Id
        });
        await db.SaveChangesAsync();

        // GET confirmation — should show recursive counts: 1 direct subfolder, 1 recursive doc
        var getResponse = await Http.GetAsync($"/Folder/DeleteFolder/{root.Id}");
        getResponse.EnsureSuccessStatusCode();
        var html = await getResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Recursively") || html.Contains("total"),
            "Should show recursive counts for nested content");

        // Cascade delete
        var token = ExtractAntiForgeryToken(html);
        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token }
        });
        await Http.PostAsync($"/Folder/DeleteFolder/{root.Id}", formData);

        using var verifyScope = Server!.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        Assert.IsNull(await verifyDb.MarkdownDocumentFolders.FindAsync(root.Id));
        Assert.IsNull(await verifyDb.MarkdownDocumentFolders.FindAsync(child.Id));
        Assert.IsFalse(await verifyDb.MarkdownDocuments.AnyAsync(d => d.FolderId == child.Id));
    }

    [TestMethod]
    public async Task FolderItemCounts_ShownInHistoryList()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var folder = new MarkdownDocumentFolder { Name = "CountedFolder", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(folder);
        await db.SaveChangesAsync();

        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(), Title = "Doc1", Content = "a", UserId = user.Id, FolderId = folder.Id
        });
        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(), Title = "Doc2", Content = "b", UserId = user.Id, FolderId = folder.Id
        });
        await db.SaveChangesAsync();

        var response = await Http.GetAsync("/Home/History");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("2 doc"), "Should show 2 docs in folder size column");
    }

    [TestMethod]
    public async Task CreateDocument_InFolder()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var folder = new MarkdownDocumentFolder { Name = "Target Folder", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(folder);
        await db.SaveChangesAsync();

        var getResponse = await Http.GetAsync($"/Home/Index?folderId={folder.Id}");
        var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());

        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "InputMarkdown", "# Doc in folder" }, { "FolderId", folder.Id.ToString() }
        });

        var response = await Http.PostAsync("/Home/SaveNew", formData);
        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);

        var doc = await db.MarkdownDocuments
            .Where(d => d.UserId == user.Id && d.FolderId == folder.Id).FirstOrDefaultAsync();
        Assert.IsNotNull(doc, "Document should be created in the specified folder");
    }

    [TestMethod]
    public async Task ExistingDocuments_RootLevel()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(), Title = "Legacy Document", Content = "old content", UserId = user.Id
        });
        await db.SaveChangesAsync();

        var response = await Http.GetAsync("/Home/History");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("Legacy Document"), "Existing document with null FolderId should appear at root");
    }

    [TestMethod]
    public async Task MoveDocument_ViaMovePage()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var folder = new MarkdownDocumentFolder { Name = "MoveTarget", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(folder);
        await db.SaveChangesAsync();

        var doc = new MarkdownDocument
        {
            Id = Guid.NewGuid(), Title = "Doc to Move", Content = "move me", UserId = user.Id
        };
        db.MarkdownDocuments.Add(doc);
        await db.SaveChangesAsync();

        // Browse into MoveTarget folder
        var browseResponse = await Http.GetAsync($"/Home/Move/{doc.Id}?browseFolderId={folder.Id}");
        browseResponse.EnsureSuccessStatusCode();
        var token = ExtractAntiForgeryToken(await browseResponse.Content.ReadAsStringAsync());

        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token }, { "targetFolderId", folder.Id.ToString() }
        });

        var postResponse = await Http.PostAsync($"/Home/Move/{doc.Id}", formData);
        Assert.AreEqual(HttpStatusCode.Redirect, postResponse.StatusCode);

        using var verifyScope = Server!.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var updated = await verifyDb.MarkdownDocuments.FindAsync(doc.Id);
        Assert.IsNotNull(updated);
        Assert.AreEqual(folder.Id, updated.FolderId);
    }

    [TestMethod]
    public async Task MoveDocument_ToRoot()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var folder = new MarkdownDocumentFolder { Name = "FromFolder", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(folder);
        await db.SaveChangesAsync();

        var doc = new MarkdownDocument
        {
            Id = Guid.NewGuid(), Title = "Move to Root", Content = "content", UserId = user.Id, FolderId = folder.Id
        };
        db.MarkdownDocuments.Add(doc);
        await db.SaveChangesAsync();

        var getResponse = await Http.GetAsync($"/Home/Move/{doc.Id}");
        var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());
        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token }, { "targetFolderId", "" }
        });

        var postResponse = await Http.PostAsync($"/Home/Move/{doc.Id}", formData);
        Assert.AreEqual(HttpStatusCode.Redirect, postResponse.StatusCode);

        using var verifyScope = Server!.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var updated = await verifyDb.MarkdownDocuments.FindAsync(doc.Id);
        Assert.IsNotNull(updated);
        Assert.IsNull(updated.FolderId);
    }

    [TestMethod]
    public async Task UserIsolation_FoldersScopedToUser()
    {
        var (emailA, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var userA = await userManager.FindByEmailAsync(emailA);
        Assert.IsNotNull(userA);

        db.MarkdownDocumentFolders.Add(new MarkdownDocumentFolder { Name = "UserA Folder", UserId = userA.Id });

        var userB = new User { UserName = "userb@test.com", Email = "userb@test.com", DisplayName = "User B" };
        await userManager.CreateAsync(userB, "TestPassword123!");
        db.MarkdownDocumentFolders.Add(new MarkdownDocumentFolder { Name = "UserB Folder", UserId = userB.Id });
        await db.SaveChangesAsync();

        var response = await Http.GetAsync("/Home/History");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("UserA Folder"));
        Assert.IsFalse(html.Contains("UserB Folder"));

        var folderB = await db.MarkdownDocumentFolders.FirstAsync(f => f.UserId == userB.Id);
        var editResponse = await Http.GetAsync($"/Folder/EditFolder/{folderB.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, editResponse.StatusCode);
    }

    [TestMethod]
    public async Task CreateFolder_WithOtherUsersParent_ReturnsNotFound()
    {
        var (emailA, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var userA = await userManager.FindByEmailAsync(emailA);
        Assert.IsNotNull(userA);

        var userB = new User { UserName = "folder-parent-b@test.com", Email = "folder-parent-b@test.com", DisplayName = "User B" };
        await userManager.CreateAsync(userB, "TestPassword123!");
        var folderB = new MarkdownDocumentFolder { Name = "UserB Parent", UserId = userB.Id };
        db.MarkdownDocumentFolders.Add(folderB);
        await db.SaveChangesAsync();

        var getResponse = await Http.GetAsync("/Folder/CreateFolder");
        var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());
        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Name", "Forged Child" },
            { "ParentFolderId", folderB.Id.ToString() }
        });

        var response = await Http.PostAsync("/Folder/CreateFolder", formData);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.IsFalse(await db.MarkdownDocumentFolders.AnyAsync(f => f.UserId == userA.Id && f.Name == "Forged Child"));
    }

    [TestMethod]
    public async Task EditFolder_MoveToOtherUsersParent_ReturnsNotFound()
    {
        var (emailA, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var userA = await userManager.FindByEmailAsync(emailA);
        Assert.IsNotNull(userA);

        var folderA = new MarkdownDocumentFolder { Name = "UserA Folder", UserId = userA.Id };
        db.MarkdownDocumentFolders.Add(folderA);
        var userB = new User { UserName = "folder-edit-b@test.com", Email = "folder-edit-b@test.com", DisplayName = "User B" };
        await userManager.CreateAsync(userB, "TestPassword123!");
        var folderB = new MarkdownDocumentFolder { Name = "UserB Parent", UserId = userB.Id };
        db.MarkdownDocumentFolders.Add(folderB);
        await db.SaveChangesAsync();

        var getResponse = await Http.GetAsync($"/Folder/EditFolder/{folderA.Id}");
        var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());
        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "Id", folderA.Id.ToString() },
            { "Name", folderA.Name },
            { "BrowseParentFolderId", folderB.Id.ToString() }
        });

        var response = await Http.PostAsync("/Folder/EditFolder", formData);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var folder = await db.MarkdownDocumentFolders.FindAsync(folderA.Id);
        Assert.IsNotNull(folder);
        Assert.IsNull(folder.ParentFolderId);
    }

    [TestMethod]
    public async Task CreateDocument_WithOtherUsersFolder_ReturnsNotFound()
    {
        await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var userB = new User { UserName = "doc-create-b@test.com", Email = "doc-create-b@test.com", DisplayName = "User B" };
        await userManager.CreateAsync(userB, "TestPassword123!");
        var folderB = new MarkdownDocumentFolder { Name = "UserB Folder", UserId = userB.Id };
        db.MarkdownDocumentFolders.Add(folderB);
        await db.SaveChangesAsync();

        var getResponse = await Http.GetAsync("/Home/Index");
        var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());
        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "InputMarkdown", "# Forged Doc" },
            { "FolderId", folderB.Id.ToString() }
        });

        var response = await Http.PostAsync("/Home/SaveNew", formData);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.IsFalse(await db.MarkdownDocuments.AnyAsync(d => d.Content == "# Forged Doc"));
    }

    [TestMethod]
    public async Task MoveDocument_ToOtherUsersFolder_ReturnsNotFound()
    {
        var (emailA, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var userA = await userManager.FindByEmailAsync(emailA);
        Assert.IsNotNull(userA);

        var doc = new MarkdownDocument
        {
            Id = Guid.NewGuid(), Title = "UserA Doc", Content = "content", UserId = userA.Id
        };
        db.MarkdownDocuments.Add(doc);
        var userB = new User { UserName = "doc-move-b@test.com", Email = "doc-move-b@test.com", DisplayName = "User B" };
        await userManager.CreateAsync(userB, "TestPassword123!");
        var folderB = new MarkdownDocumentFolder { Name = "UserB Folder", UserId = userB.Id };
        db.MarkdownDocumentFolders.Add(folderB);
        await db.SaveChangesAsync();

        var getResponse = await Http.GetAsync($"/Home/Move/{doc.Id}");
        var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());
        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "targetFolderId", folderB.Id.ToString() }
        });

        var response = await Http.PostAsync($"/Home/Move/{doc.Id}", formData);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var updated = await db.MarkdownDocuments.FindAsync(doc.Id);
        Assert.IsNotNull(updated);
        Assert.IsNull(updated.FolderId);
    }

    [TestMethod]
    public async Task RaceCondition_EditFolderAfterDelete()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var folder = new MarkdownDocumentFolder { Name = "EditAfterDelete", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(folder);
        await db.SaveChangesAsync();

        var getResponse = await Http.GetAsync($"/Folder/EditFolder/{folder.Id}");
        var editToken = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());

        // Delete
        var delResponse = await Http.GetAsync($"/Folder/DeleteFolder/{folder.Id}");
        var delToken = ExtractAntiForgeryToken(await delResponse.Content.ReadAsStringAsync());
        var delForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", delToken }
        });
        await Http.PostAsync($"/Folder/DeleteFolder/{folder.Id}", delForm);

        // Try to edit deleted folder
        var editForm = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", editToken },
            { "Id", folder.Id.ToString() }, { "Name", "Renamed After Delete" }
        });
        var editResponse = await Http.PostAsync("/Folder/EditFolder", editForm);
        Assert.AreEqual(HttpStatusCode.NotFound, editResponse.StatusCode);
    }

    [TestMethod]
    public async Task RaceCondition_ConcurrentFolderDelete()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var folder = new MarkdownDocumentFolder { Name = "ConcurrentDelete", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(folder);
        await db.SaveChangesAsync();

        var getResponse = await Http.GetAsync($"/Folder/DeleteFolder/{folder.Id}");
        var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());
        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token }
        });

        var response1 = await Http.PostAsync($"/Folder/DeleteFolder/{folder.Id}", formData);
        Assert.AreEqual(HttpStatusCode.Redirect, response1.StatusCode);

        var response2 = await Http.PostAsync($"/Folder/DeleteFolder/{folder.Id}", formData);
        Assert.AreEqual(HttpStatusCode.NotFound, response2.StatusCode);
    }

    [TestMethod]
    public async Task RaceCondition_SameFolderName_DifferentUsers()
    {
        var (emailA, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var userA = await userManager.FindByEmailAsync(emailA);
        Assert.IsNotNull(userA);

        db.MarkdownDocumentFolders.Add(new MarkdownDocumentFolder { Name = "Work", UserId = userA.Id });
        var userB = new User { UserName = "userb-race@test.com", Email = "userb-race@test.com", DisplayName = "User B" };
        await userManager.CreateAsync(userB, "TestPassword123!");
        db.MarkdownDocumentFolders.Add(new MarkdownDocumentFolder { Name = "Work", UserId = userB.Id });
        await db.SaveChangesAsync();

        var countA = await db.MarkdownDocumentFolders.CountAsync(f => f.Name == "Work" && f.UserId == userA.Id);
        var countB = await db.MarkdownDocumentFolders.CountAsync(f => f.Name == "Work" && f.UserId == userB.Id);
        Assert.AreEqual(1, countA);
        Assert.AreEqual(1, countB);

        var response = await Http.GetAsync("/Home/History");
        var html = await response.Content.ReadAsStringAsync();
        var workCount = System.Text.RegularExpressions.Regex.Matches(html, "Work").Count;
        Assert.AreEqual(1, workCount, "User A should only see ONE 'Work' folder");
    }

    [TestMethod]
    public async Task FolderNameTrimming_WhitespaceOnly()
    {
        await RegisterAndLoginAsync();

        var getResponse = await Http.GetAsync("/Folder/CreateFolder");
        var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());

        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token }, { "Name", "   " }
        });

        var response = await Http.PostAsync("/Folder/CreateFolder", formData);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(html.Contains("required") || html.Contains("Required") || html.Contains("error"));
    }

    [TestMethod]
    public async Task SearchDocuments_WithinFolder()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        db.MarkdownDocuments.Add(new MarkdownDocument
            { Id = Guid.NewGuid(), Title = "Alpha", Content = "first", UserId = user.Id });
        db.MarkdownDocuments.Add(new MarkdownDocument
            { Id = Guid.NewGuid(), Title = "Beta", Content = "second", UserId = user.Id });
        await db.SaveChangesAsync();

        var response = await Http.GetAsync("/Home/History?search=Alpha");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        Assert.IsTrue(html.Contains("Alpha"));
        Assert.IsFalse(html.Contains("Beta"));
    }

    [TestMethod]
    public async Task BrowseNestedFolders_ThreeLevels()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var root = new MarkdownDocumentFolder { Name = "Level1", UserId = user.Id };
        db.MarkdownDocumentFolders.Add(root);
        await db.SaveChangesAsync();
        var level2 = new MarkdownDocumentFolder { Name = "Level2", UserId = user.Id, ParentFolderId = root.Id };
        db.MarkdownDocumentFolders.Add(level2);
        await db.SaveChangesAsync();
        var level3 = new MarkdownDocumentFolder { Name = "Level3", UserId = user.Id, ParentFolderId = level2.Id };
        db.MarkdownDocumentFolders.Add(level3);
        await db.SaveChangesAsync();

        db.MarkdownDocuments.Add(new MarkdownDocument
        {
            Id = Guid.NewGuid(), Title = "Deep Doc", Content = "nested", UserId = user.Id, FolderId = level3.Id
        });
        await db.SaveChangesAsync();

        var r1 = await Http.GetAsync($"/Home/History?folderId={root.Id}");
        r1.EnsureSuccessStatusCode();
        var h1 = await r1.Content.ReadAsStringAsync();
        Assert.IsTrue(h1.Contains("Level2"));
        Assert.IsFalse(h1.Contains("Level3"));

        var r2 = await Http.GetAsync($"/Home/History?folderId={level2.Id}");
        r2.EnsureSuccessStatusCode();
        var h2 = await r2.Content.ReadAsStringAsync();
        Assert.IsTrue(h2.Contains("Level3"));

        var r3 = await Http.GetAsync($"/Home/History?folderId={level3.Id}");
        r3.EnsureSuccessStatusCode();
        var h3 = await r3.Content.ReadAsStringAsync();
        Assert.IsTrue(h3.Contains("Deep Doc"));
    }

    [TestMethod]
    public async Task CreateDocument_WithFolderId_ShowsInCorrectFolder()
    {
        var (email, _) = await RegisterAndLoginAsync();

        using var scope = Server!.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.IsNotNull(user);

        var folderA = new MarkdownDocumentFolder { Name = "FolderA", UserId = user.Id };
        var folderB = new MarkdownDocumentFolder { Name = "FolderB", UserId = user.Id };
        db.MarkdownDocumentFolders.AddRange(folderA, folderB);
        await db.SaveChangesAsync();

        var getResponse = await Http.GetAsync($"/Home/Index?folderId={folderA.Id}");
        var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());
        var formData = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "__RequestVerificationToken", token },
            { "InputMarkdown", "# In FolderA" }, { "FolderId", folderA.Id.ToString() }
        });
        await Http.PostAsync("/Home/SaveNew", formData);

        var rA = await Http.GetAsync($"/Home/History?folderId={folderA.Id}");
        var hA = await rA.Content.ReadAsStringAsync();
        Assert.IsTrue(hA.Contains("In FolderA"));

        var rB = await Http.GetAsync($"/Home/History?folderId={folderB.Id}");
        var hB = await rB.Content.ReadAsStringAsync();
        Assert.IsFalse(hB.Contains("In FolderA"));

        var rRoot = await Http.GetAsync("/Home/History");
        var hRoot = await rRoot.Content.ReadAsStringAsync();
        Assert.IsFalse(hRoot.Contains("In FolderA"));
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = System.Text.RegularExpressions.Regex.Match(html,
            @"<input[^>]*name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }
}
