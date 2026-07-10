using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Models.TagViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.MarkToHtml.Controllers;

[Authorize]
public class TagsController(
    TemplateDbContext context,
    UserManager<User> userManager) : Controller
{
    /// <summary>
    /// GET: Show all tags for the current user.
    /// </summary>
    public async Task<IActionResult> Index()
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var tags = await context.Tags
            .Where(t => t.UserId == userId)
            .OrderBy(t => t.Name)
            .ToListAsync();

        // Count documents for each tag
        var tagsWithCounts = new List<Tag>();
        foreach (var tag in tags)
        {
            var docCount = await context.DocumentTags
                .CountAsync(dt => dt.TagId == tag.Id);
            tag.DocumentCount = docCount;
            tagsWithCounts.Add(tag);
        }

        var model = new IndexTagViewModel
        {
            Tags = tagsWithCounts
        };

        return View(model);
    }

    /// <summary>
    /// GET: Show create tag form.
    /// </summary>
    public IActionResult CreateTag()
    {
        return View();
    }

    /// <summary>
    /// POST: Create a new tag.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateTag(CreateTagViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        // Check for duplicate tag name
        var nameExists = await context.Tags
            .AnyAsync(t => t.UserId == userId && t.Name == model.Name);
        if (nameExists)
        {
            ModelState.AddModelError(nameof(model.Name), "A tag with this name already exists.");
            return View(model);
        }

        var tag = new Tag
        {
            Name = model.Name,
            Color = model.Color,
            UserId = userId
        };
        context.Tags.Add(tag);
        await context.SaveChangesAsync();
        return RedirectToAction("Index");
    }

    /// <summary>
    /// GET: Show edit tag form.
    /// </summary>
    public async Task<IActionResult> EditTag(int id)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var tag = await context.Tags
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (tag == null) return NotFound();

        var model = new EditTagViewModel
        {
            Id = tag.Id,
            Name = tag.Name,
            Color = tag.Color
        };

        return View(model);
    }

    /// <summary>
    /// POST: Edit a tag.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditTag(EditTagViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var tag = await context.Tags
            .FirstOrDefaultAsync(t => t.Id == model.Id && t.UserId == userId);

        if (tag == null) return NotFound();

        // Check for duplicate tag name (excluding self)
        var nameExists = await context.Tags
            .AnyAsync(t => t.Id != model.Id && t.UserId == userId && t.Name == model.Name);
        if (nameExists)
        {
            ModelState.AddModelError(nameof(model.Name), "A tag with this name already exists.");
            return View(model);
        }

        tag.Name = model.Name;
        tag.Color = model.Color;
        await context.SaveChangesAsync();
        return RedirectToAction("Index");
    }

    /// <summary>
    /// GET: Show delete confirmation page.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> DeleteTag(int id)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var tag = await context.Tags
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (tag == null) return NotFound();

        var docCount = await context.DocumentTags
            .CountAsync(dt => dt.TagId == id);

        var model = new DeleteTagViewModel
        {
            Id = tag.Id,
            Name = tag.Name,
            DocumentCount = docCount
        };

        return View(model);
    }

    /// <summary>
    /// POST: Delete a tag.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ActionName("DeleteTag")]
    public async Task<IActionResult> DeleteTagConfirmed(int id)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var tag = await context.Tags
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (tag == null) return NotFound();

        // Delete all document-tag links
        var docTagsToDelete = await context.DocumentTags
            .Where(dt => dt.TagId == id)
            .ToListAsync();
        context.DocumentTags.RemoveRange(docTagsToDelete);

        // Delete the tag
        context.Tags.Remove(tag);
        await context.SaveChangesAsync();

        return RedirectToAction("Index");
    }

    /// <summary>
    /// POST: Add tag to a document via AJAX.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddTagToDocument(Guid documentId, int tagId)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        // Check if the tag belongs to the user
        var tag = await context.Tags
            .FirstOrDefaultAsync(t => t.Id == tagId && t.UserId == userId);
        if (tag == null)
        {
            return NotFound("Tag not found or you don't have permission.");
        }

        // Check if the document exists and belongs to the user
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId);
        if (document == null)
        {
            return NotFound("Document not found.");
        }

        // Check if the link already exists
        var exists = await context.DocumentTags
            .AnyAsync(dt => dt.DocumentId == documentId && dt.TagId == tagId);
        if (!exists)
        {
            var docTag = new DocumentTag
            {
                DocumentId = documentId,
                TagId = tagId
            };
            context.DocumentTags.Add(docTag);
            await context.SaveChangesAsync();
        }

        return Ok(new { success = true });
    }

    /// <summary>
    /// POST: Remove tag from a document via AJAX.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveTagFromDocument(Guid documentId, int tagId)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        var docTag = await context.DocumentTags
            .FirstOrDefaultAsync(dt => dt.DocumentId == documentId && dt.TagId == tagId);
        if (docTag != null)
        {
            context.DocumentTags.Remove(docTag);
            await context.SaveChangesAsync();
        }

        return Ok(new { success = true });
    }
}