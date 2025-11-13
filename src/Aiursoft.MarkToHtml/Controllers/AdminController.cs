using Aiursoft.CSTools.Tools;
using Aiursoft.MarkToHtml.Authorization;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using Aiursoft.MarkToHtml.Models.AdminViewModels;
using Aiursoft.WebTools.Attributes;

namespace Aiursoft.MarkToHtml.Controllers;

/// <summary>
/// This controller is used for administrative actions related to documents.
/// </summary>
[Authorize]
[LimitPerMin]
public class AdminController(
    IStringLocalizer<AdminController> localizer,
    UserManager<User> userManager,
    TemplateDbContext context)
    : Controller
{
    /// <summary>
    /// Displays a list of all markdown documents in the system.
    /// This action requires the 'CanReadAllDocuments' permission.
    /// </summary>
    [Authorize(Policy = AppPermissionNames.CanReadAllDocuments)]
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Documents",
        CascadedLinksIcon = "server",
        CascadedLinksOrder = 1,
        LinkText = "All Documents",
        LinkOrder = 1)]
    public async Task<IActionResult> AllDocuments([FromQuery] string? search)
    {
        var trimmedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var documentsQuery = context.MarkdownDocuments
            .Include(d => d.User)
            .AsQueryable();

        if (trimmedSearch != null)
        {
            var likePattern = $"%{trimmedSearch}%";
            documentsQuery = documentsQuery.Where(d =>
                (d.Title != null && EF.Functions.Like(d.Title, likePattern)) ||
                (d.Content != null && EF.Functions.Like(d.Content!, likePattern)) ||
                ((
                    (EF.Functions.Like(d.User.DisplayName, likePattern)) ||
                    (d.User.UserName != null && EF.Functions.Like(d.User.UserName, likePattern)))));
        }

        var allDocuments = await documentsQuery
            .OrderByDescending(d => d.CreationTime)
            .ToListAsync();

        return this.StackView(new AllDocumentsViewModel
        {
            AllDocuments = allDocuments,
            SearchQuery = trimmedSearch
        });
    }

    /// <summary>
    /// Displays a list of markdown documents for a specific user.
    /// This action requires the 'CanReadAllDocuments' permission.
    /// </summary>
    [Authorize(Policy = AppPermissionNames.CanReadAllDocuments)]
    public async Task<IActionResult> UserDocuments([FromRoute] string? id, [FromQuery] string? search)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return NotFound("User ID is required.");
        }

        var user = await userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var trimmedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var documentsQuery = context.MarkdownDocuments
            .Where(d => d.UserId == id);

        if (trimmedSearch != null)
        {
            var likePattern = $"%{trimmedSearch}%";
            documentsQuery = documentsQuery.Where(d =>
                (d.Title != null && EF.Functions.Like(d.Title, likePattern)) ||
                (d.Content != null && EF.Functions.Like(d.Content!, likePattern)));
        }

        var documents = await documentsQuery
            .OrderByDescending(d => d.CreationTime)
            .ToListAsync();

        var model = new UserDocumentsViewModel
        {
            User = user,
            UserDocuments = documents,
            SearchQuery = trimmedSearch
        };

        return this.StackView(model);
    }

    /// <summary>
    /// Allows an administrator to edit any document, including its owner.
    /// This action requires the 'CanEditAnyDocument' permission.
    /// </summary>
    [Authorize(Policy = AppPermissionNames.CanEditAnyDocument)]
    public async Task<IActionResult> EditDocument([FromRoute] Guid id, [FromQuery] bool? saved = false)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("Document not found.");
        }

        var allUsers = await userManager.Users
            .OrderBy(u => u.UserName)
            .ToListAsync();

        var model = new EditDocumentViewModel
        {
            DocumentId = document.Id,
            Title = document.Title,
            InputMarkdown = document.Content ?? string.Empty,
            SelectedUserId = document.UserId,
            AllUsers = allUsers.Select(user => new SelectListItem
            {
                Value = user.Id,
                Text = user.UserName,
                Selected = user.Id == document.UserId
            }).ToList(),
            SavedSuccessfully = saved ?? false
        };

        return this.StackView(model);
    }

    /// <summary>
    /// Saves the changes to a document from an administrator, including the owner.
    /// This action requires the 'CanEditAnyDocument' permission.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditAnyDocument)]
    public async Task<IActionResult> EditDocument(EditDocumentViewModel model)
    {
        var newOwner = await userManager.FindByIdAsync(model.SelectedUserId);
        if (!ModelState.IsValid || newOwner == null)
        {
            if (newOwner == null)
            {
                ModelState.AddModelError(nameof(model.SelectedUserId), localizer["The selected new owner does not exist."]);
            }
            var allUsers = await userManager.Users.OrderBy(u => u.UserName).ToListAsync();
            model.AllUsers = allUsers.Select(user => new SelectListItem
            {
                Value = user.Id,
                Text = user.UserName,
                Selected = user.Id == model.SelectedUserId
            }).ToList();
            return this.StackView(model);
        }

        var documentInDb = await context.MarkdownDocuments.FirstOrDefaultAsync(d => d.Id == model.DocumentId);
        if (documentInDb == null)
        {
            return NotFound("Document not found.");
        }
        documentInDb.Content = model.InputMarkdown.SafeSubstring(65535);
        documentInDb.Title = model.Title;
        documentInDb.UserId = model.SelectedUserId;

        await context.SaveChangesAsync();
        return RedirectToAction(nameof(EditDocument), new { id = model.DocumentId, saved = true });
    }

    /// <summary>
    /// Displays a confirmation page before an administrator deletes a document.
    /// This action requires the 'CanDeleteAnyDocument' permission.
    /// </summary>
    [Authorize(Policy = AppPermissionNames.CanDeleteAnyDocument)]
    public async Task<IActionResult> DeleteDocument([FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("Document not found.");
        }

        return this.StackView(new DeleteDocumentViewModel
        {
            Document = document
        });
    }

    /// <summary>
    /// Deletes a document from the database.
    /// This action requires the 'CanDeleteAnyDocument' permission.
    /// </summary>
    [HttpPost, ActionName("DeleteDocument")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanDeleteAnyDocument)]
    public async Task<IActionResult> DeleteDocumentConfirmed([FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments.FirstOrDefaultAsync(d => d.Id == id);
        if (document == null)
        {
            return NotFound("Document not found.");
        }

        context.MarkdownDocuments.Remove(document);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(AllDocuments));
    }
}
