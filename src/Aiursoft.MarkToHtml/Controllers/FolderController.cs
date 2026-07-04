using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Models.FolderViewModels;
using Aiursoft.MarkToHtml.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.Controllers;

[Authorize]
public class FolderController(
    TemplateDbContext context,
    UserManager<User> userManager) : Controller
{
    /// <summary>
    /// Browse folders and documents at a given level. Null means root.
    /// </summary>
    public async Task<IActionResult> Index(int? id, [FromQuery] string? search)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var currentFolder = id.HasValue
            ? await context.MarkdownDocumentFolders
                .FirstOrDefaultAsync(f => f.Id == id.Value && f.UserId == userId)
            : null;

        if (id.HasValue && currentFolder == null)
        {
            return NotFound("Folder not found.");
        }

        var subFolders = await context.MarkdownDocumentFolders
            .Where(f => f.ParentFolderId == id && f.UserId == userId)
            .OrderBy(f => f.Name)
            .ToListAsync();

        var trimmedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var documentsQuery = context.MarkdownDocuments
            .Where(d => d.UserId == userId && d.FolderId == id);

        if (trimmedSearch != null)
        {
            documentsQuery = documentsQuery.Where(d =>
                (d.Title != null && d.Title.Contains(trimmedSearch)) ||
                (d.Content != null && d.Content.Contains(trimmedSearch)));
        }

        var documents = await documentsQuery
            .Include(d => d.DocumentShares)
            .OrderByDescending(d => d.CreationTime)
            .ToListAsync();

        // Build breadcrumb path from root to parent of current folder
        var breadcrumb = new List<MarkdownDocumentFolder>();
        if (currentFolder != null)
        {
            var ancestor = currentFolder.ParentFolderId.HasValue
                ? await context.MarkdownDocumentFolders
                    .FirstOrDefaultAsync(f => f.Id == currentFolder.ParentFolderId.Value && f.UserId == userId)
                : null;

            while (ancestor != null)
            {
                breadcrumb.Insert(0, ancestor); // prepend to get root→leaf order
                ancestor = ancestor.ParentFolderId.HasValue
                    ? await context.MarkdownDocumentFolders
                        .FirstOrDefaultAsync(f => f.Id == ancestor.ParentFolderId.Value && f.UserId == userId)
                    : null;
            }
        }

        var model = new FolderIndexViewModel
        {
            FolderId = id,
            CurrentFolder = currentFolder,
            Breadcrumb = breadcrumb,
            SubFolders = subFolders,
            Documents = documents,
            SearchQuery = trimmedSearch
        };
        return this.StackView(model);
    }

    /// <summary>
    /// GET: Show create folder form.
    /// </summary>
    public IActionResult CreateFolder(int? id)
    {
        return this.StackView(new CreateFolderViewModel { ParentFolderId = id });
    }

    /// <summary>
    /// POST: Create a new folder.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFolder(CreateFolderViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return this.StackView(model);
        }

        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        // Check for duplicate folder name at the same level
        var nameExists = await context.MarkdownDocumentFolders
            .AnyAsync(f => f.UserId == userId
                        && f.ParentFolderId == model.ParentFolderId
                        && f.Name == model.Name);
        if (nameExists)
        {
            ModelState.AddModelError(nameof(model.Name), "A folder with this name already exists in this location.");
            return this.StackView(model);
        }

        var folder = new MarkdownDocumentFolder
        {
            Name = model.Name,
            ParentFolderId = model.ParentFolderId,
            UserId = userId
        };
        context.MarkdownDocumentFolders.Add(folder);
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { id = model.ParentFolderId });
    }

    /// <summary>
    /// GET: Browse to select a new parent folder. Navigate one level at a time.
    /// </summary>
    public async Task<IActionResult> EditFolder(int id, [FromQuery] int? browseFolderId)
    {
        var userId = userManager.GetUserId(User);
        var folder = await context.MarkdownDocumentFolders
            .FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId);

        if (folder == null) return NotFound();

        // Default to current parent if not browsing elsewhere
        var effectiveBrowseId = browseFolderId ?? folder.ParentFolderId;

        var browseFolder = effectiveBrowseId.HasValue
            ? await context.MarkdownDocumentFolders
                .FirstOrDefaultAsync(f => f.Id == effectiveBrowseId.Value && f.UserId == userId)
            : null;

        var subFolders = await context.MarkdownDocumentFolders
            .Where(f => f.ParentFolderId == effectiveBrowseId && f.UserId == userId)
            .OrderBy(f => f.Name)
            .ToListAsync();

        // Build breadcrumb from root to browsed folder
        var breadcrumb = new List<MarkdownDocumentFolder>();
        if (browseFolder != null)
        {
            var ancestor = browseFolder.ParentFolderId.HasValue
                ? await context.MarkdownDocumentFolders
                    .FirstOrDefaultAsync(f => f.Id == browseFolder.ParentFolderId.Value && f.UserId == userId)
                : null;

            while (ancestor != null)
            {
                breadcrumb.Insert(0, ancestor);
                ancestor = ancestor.ParentFolderId.HasValue
                    ? await context.MarkdownDocumentFolders
                        .FirstOrDefaultAsync(f => f.Id == ancestor.ParentFolderId.Value && f.UserId == userId)
                    : null;
            }
        }

        return this.StackView(new EditFolderViewModel
        {
            Id = folder.Id,
            Name = folder.Name,
            BrowseParentFolderId = effectiveBrowseId,
            BrowseFolder = browseFolder,
            SubFolders = subFolders,
            Breadcrumb = breadcrumb
        });
    }

    /// <summary>
    /// POST: Edit folder name or move to the selected parent folder.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditFolder(EditFolderViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return await RebuildEditFolderView(model);
        }

        var userId = userManager.GetUserId(User);
        var folder = await context.MarkdownDocumentFolders
            .FirstOrDefaultAsync(f => f.Id == model.Id && f.UserId == userId);

        if (folder == null) return NotFound();

        var newParentId = model.BrowseParentFolderId;

        if (await IsFolderChildOf(folder.Id, newParentId))
        {
            ModelState.AddModelError(string.Empty, "Cannot move a folder to its own child!");
            return await RebuildEditFolderView(model);
        }

        // Check for duplicate folder name at the same level (exclude self)
        var nameExists = await context.MarkdownDocumentFolders
            .AnyAsync(f => f.Id != model.Id
                        && f.UserId == userId
                        && f.ParentFolderId == newParentId
                        && f.Name == model.Name);
        if (nameExists)
        {
            ModelState.AddModelError(nameof(model.Name), "A folder with this name already exists in this location.");
            return await RebuildEditFolderView(model);
        }

        folder.Name = model.Name;
        folder.ParentFolderId = newParentId;
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { id = newParentId });
    }

    private async Task<IActionResult> RebuildEditFolderView(EditFolderViewModel model)
    {
        var userId = userManager.GetUserId(User);
        var browseFolder = model.BrowseParentFolderId.HasValue
            ? await context.MarkdownDocumentFolders
                .FirstOrDefaultAsync(f => f.Id == model.BrowseParentFolderId.Value && f.UserId == userId)
            : null;

        var subFolders = await context.MarkdownDocumentFolders
            .Where(f => f.ParentFolderId == model.BrowseParentFolderId && f.UserId == userId)
            .OrderBy(f => f.Name)
            .ToListAsync();

        var breadcrumb = new List<MarkdownDocumentFolder>();
        if (browseFolder != null)
        {
            var ancestor = browseFolder.ParentFolderId.HasValue
                ? await context.MarkdownDocumentFolders
                    .FirstOrDefaultAsync(f => f.Id == browseFolder.ParentFolderId.Value && f.UserId == userId)
                : null;

            while (ancestor != null)
            {
                breadcrumb.Insert(0, ancestor);
                ancestor = ancestor.ParentFolderId.HasValue
                    ? await context.MarkdownDocumentFolders
                        .FirstOrDefaultAsync(f => f.Id == ancestor.ParentFolderId.Value && f.UserId == userId)
                    : null;
            }
        }

        model.BrowseFolder = browseFolder;
        model.SubFolders = subFolders;
        model.Breadcrumb = breadcrumb;
        return this.StackView(model);
    }

    /// <summary>
    /// POST: Delete an empty folder.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFolder(int id)
    {
        var userId = userManager.GetUserId(User);
        var folder = await context.MarkdownDocumentFolders
            .Include(f => f.SubFolders)
            .Include(f => f.MarkdownDocuments)
            .FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId);

        if (folder == null) return NotFound();

        if (folder.SubFolders.Any() || folder.MarkdownDocuments.Any())
        {
            return BadRequest("Folder is not empty.");
        }

        var parentId = folder.ParentFolderId;
        context.MarkdownDocumentFolders.Remove(folder);
        await context.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { id = parentId });
    }

    /// <summary>
    /// Recursively check if targetFolderId is a child (or the same as) sourceFolderId.
    /// Used to prevent circular references when moving folders.
    /// </summary>
    private async Task<bool> IsFolderChildOf(int sourceFolderId, int? targetFolderId)
    {
        if (targetFolderId == null) return false;
        if (sourceFolderId == targetFolderId) return true;

        var targetFolder = await context.MarkdownDocumentFolders.FindAsync(targetFolderId.Value);
        if (targetFolder == null) return false;

        return await IsFolderChildOf(sourceFolderId, targetFolder.ParentFolderId);
    }
}
