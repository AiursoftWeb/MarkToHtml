using System.ComponentModel.DataAnnotations;
using Aiursoft.CSTools.Tools;
using Aiursoft.MarkToHtml.Models.HomeViewModels;
using Aiursoft.MarkToHtml.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.WebTools.Attributes;
using Aiursoft.MarkToHtml.Authorization;


namespace Aiursoft.MarkToHtml.Controllers;

[LimitPerMin]
public class HomeController(
    ILogger<HomeController> logger,
    UserManager<User> userManager,
    TemplateDbContext context,
    MarkToHtmlService mtohService,
    IAuthorizationService authorizationService) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Home",
        CascadedLinksIcon = "home",
        CascadedLinksOrder = 1,
        LinkText = "Convert Document",
        LinkOrder = 1
    )]
    public IActionResult Index()
    {
        return this.StackView(new IndexViewModel("Untitled Document"));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(IndexViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return this.StackView(model);
        }

        var userId = userManager.GetUserId(User);
        if (User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(userId))
        {
            // If the user is authenticated, this action only saves the document in the database. And it's `edit` action to render it.
            // And go to the edit page.
            logger.LogTrace("Authenticated user submitted a document with ID: '{Id}'. Save it to the database.",
                model.DocumentId);
            var documentInDb = await context.MarkdownDocuments
                .FirstOrDefaultAsync(d => d.Id == model.DocumentId);
            var isExistingDocument = documentInDb != null;

            if (documentInDb != null)
            {
                // Check permissions for existing document
                bool isOwner = documentInDb.UserId == userId;
                bool canEdit = isOwner;

                if (!isOwner)
                {
                    // Check if user has Editable permission
                    var userRoles = await context.UserRoles
                        .Where(ur => ur.UserId == userId)
                        .Select(ur => ur.RoleId)
                        .ToListAsync();

                    canEdit = await context.DocumentShares
                        .AnyAsync(s => s.DocumentId == model.DocumentId &&
                                      s.Permission == SharePermission.Editable &&
                                      (s.SharedWithUserId == userId ||
                                       (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));
                }

                if (!canEdit)
                {
                    return Forbid();
                }

                logger.LogInformation("Updating the document with ID: '{Id}'.", model.DocumentId);
                documentInDb.Content = model.InputMarkdown.SafeSubstring(65535);
                documentInDb.Title = model.Title;
            }
            else
            {
                logger.LogInformation("Creating a new document with ID: '{Id}'.", model.DocumentId);
                model.DocumentId = Guid.NewGuid();
                var newDocument = new MarkdownDocument
                {
                    Id = model.DocumentId,
                    Content = model.InputMarkdown.SafeSubstring(65535),
                    Title = model.InputMarkdown.SafeSubstring(40),
                    UserId = userId
                };
                context.MarkdownDocuments.Add(newDocument);
            }

            await context.SaveChangesAsync();
            return RedirectToAction(nameof(Edit), new { id = model.DocumentId, saved = isExistingDocument });
        }
        else
        {
            // If the user is not authenticated, just show the result.
            logger.LogInformation(
                "An anonymous user submitted a document with ID: '{Id}'. It was not saved to the database.",
                model.DocumentId);
            model.OutputHtml = mtohService.ConvertMarkdownToHtml(model.InputMarkdown);
            return this.StackView(model);
        }
    }

    [Authorize]
    public async Task<IActionResult> Edit([Required][FromRoute] Guid id, [FromQuery] bool? saved = false)
    {
        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .Include(d => d.DocumentShares)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        // Check if user is the owner
        bool isOwner = document.UserId == userId;
        bool canEdit = isOwner;

        if (!isOwner)
        {
            // Check if document is shared with the user with Editable permission
            var userRoles = await context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            canEdit = await context.DocumentShares
                .AnyAsync(s => s.DocumentId == id &&
                              s.Permission == SharePermission.Editable &&
                              (s.SharedWithUserId == userId ||
                               (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));
        }

        if (!canEdit)
        {
            return Forbid();
        }

        var publicLink = Url.Action(nameof(PublicController.View), "Public", new { id = document.Id }, Request.Scheme);

        var model = new IndexViewModel(document.Title ?? "Empty Document")
        {
            DocumentId = document.Id,
            Title = document.Title,
            InputMarkdown = document.Content ?? string.Empty,
            OutputHtml = mtohService.ConvertMarkdownToHtml(document.Content ?? string.Empty),
            IsEditing = true,
            SavedSuccessfully = saved ?? false,
            IsPublic = document.IsPublic,
            PublicLink = publicLink,
            HasInternalShares = document.DocumentShares.Any()
        };

        return this.StackView(model: model, viewName: nameof(Index)); // Reuse the Index view for editing.
    }

    [Authorize]
    [RenderInNavBar(
    NavGroupName = "Features",
    NavGroupOrder = 1,
    CascadedLinksGroupName = "Home",
    CascadedLinksIcon = "history",
    CascadedLinksOrder = 2,
    LinkText = "My documents",
    LinkOrder = 2)]
    public async Task<IActionResult> History([FromQuery] string? search)
    {
        var userId = userManager.GetUserId(User);
        var trimmedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var documentsQuery = context.MarkdownDocuments
            .Where(d => d.UserId == userId);

        if (trimmedSearch != null)
        {
            var likePattern = $"%{trimmedSearch}%";
            documentsQuery = documentsQuery.Where(d =>
                (d.Title != null && EF.Functions.Like(d.Title, likePattern)) ||
                (d.Content != null && EF.Functions.Like(d.Content!, likePattern)));
        }

        var documents = await documentsQuery
            .Include(d => d.DocumentShares)
            .OrderByDescending(d => d.CreationTime)
            .ToListAsync();

        var model = new HistoryViewModel
        {
            MyDocuments = documents,
            SearchQuery = trimmedSearch
        };
        return this.StackView(model);
    }

    // GET: /Home/Delete/{guid}
    [Authorize]
    public async Task<IActionResult> Delete(Guid? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

        if (document == null)
        {
            // Document not found or user does not have permission.
            return NotFound();
        }

        return this.StackView(new DeleteViewModel
        {
            Document = document
        });
    }

    // POST: /Home/Delete/{guid}
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

        if (document == null)
        {
            return NotFound();
        }

        context.MarkdownDocuments.Remove(document);
        await context.SaveChangesAsync();

        logger.LogInformation("Document with ID: '{Id}' was deleted by user: '{UserId}'.", id, userId);

        return RedirectToAction(nameof(History));
    }

    /// <summary>
    /// Make a document public.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MakePublic([Required][FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        var userId = userManager.GetUserId(User);
        var isOwner = document.UserId == userId;
        var canManage = isOwner || (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyShare)).Succeeded;

        if (!canManage)
        {
            return NotFound("The document was not found or you do not have permission to modify it.");
        }

        if (!document.IsPublic)
        {
            document.IsPublic = true;
            await context.SaveChangesAsync();
            logger.LogInformation("Document with ID: '{DocumentId}' was made public by user: '{UserId}'.",
                id, userId);
        }

        return Ok();
    }

    /// <summary>
    /// Make a document private.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MakePrivate([Required][FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        var userId = userManager.GetUserId(User);
        var isOwner = document.UserId == userId;
        var canManage = isOwner || (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyShare)).Succeeded;

        if (!canManage)
        {
            return NotFound("The document was not found or you do not have permission to modify it.");
        }

        if (document.IsPublic)
        {
            document.IsPublic = false;
            await context.SaveChangesAsync();
            logger.LogInformation("Document with ID: '{DocumentId}' was made private by user: '{UserId}'.",
                id, userId);
        }

        return Ok();
    }

    /// <summary>
    /// Update document visibility (Public or Private)
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateVisibility([Required][FromRoute] Guid id, [FromForm] bool publicAccess)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        var userId = userManager.GetUserId(User);
        var isOwner = document.UserId == userId;
        var canManage = isOwner || (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyShare)).Succeeded;

        if (!canManage)
        {
            return NotFound("The document was not found or you do not have permission to modify it.");
        }

        document.IsPublic = publicAccess;
        logger.LogInformation("Document with ID: '{DocumentId}' visibility updated to {IsPublic} by user: '{UserId}'.",
            id, document.IsPublic, userId);

        await context.SaveChangesAsync();
        return RedirectToAction(nameof(ManageShares), new { id });
    }

    /// <summary>
    /// GET: Manage shares for a specific document
    /// </summary>
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> ManageShares([Required][FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments
            .Include(d => d.DocumentShares)
                .ThenInclude(s => s.SharedWithUser)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        var userId = userManager.GetUserId(User);
        var isOwner = document.UserId == userId;
        var canManage = isOwner || (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyShare)).Succeeded;

        if (!canManage)
        {
            return NotFound("The document was not found or you do not have permission to modify it.");
        }

        var allRoles = await context.Roles.ToListAsync();
        var model = new ManageSharesViewModel(document.Title ?? "Untitled Document")
        {
            DocumentId = document.Id,
            DocumentTitle = document.Title ?? "Untitled Document",
            IsPublic = document.IsPublic,
            PublicLink = Url.Action(nameof(PublicController.View), "Public", new { id = document.Id }, Request.Scheme),
            ExistingShares = document.DocumentShares.ToList(),
            AvailableRoles = allRoles
        };

        return this.StackView(model);
    }

    /// <summary>
    /// POST: Add a new share for a document
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddShare([Required][FromRoute] Guid id, AddShareViewModel model)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        var userId = userManager.GetUserId(User);
        var isOwner = document.UserId == userId;
        var canManage = isOwner || (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyShare)).Succeeded;

        if (!canManage)
        {
            return NotFound("The document was not found or you do not have permission to modify it.");
        }

        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var targetUserId = string.IsNullOrWhiteSpace(model.TargetUserId) ? null : model.TargetUserId;
        var targetRoleId = string.IsNullOrWhiteSpace(model.TargetRoleId) ? null : model.TargetRoleId;

        if (targetUserId == null && targetRoleId == null)
        {
            return RedirectToAction(nameof(ManageShares), new { id, error = "invalid" });
        }

        var exists = await context.DocumentShares
            .AnyAsync(s => s.DocumentId == document.Id &&
                           ((targetUserId != null && s.SharedWithUserId == targetUserId) ||
                            (targetRoleId != null && s.SharedWithRoleId == targetRoleId)));

        if (exists)
        {
            return RedirectToAction(nameof(ManageShares), new { id, error = "duplicate" });
        }

        var share = new DocumentShare
        {
            DocumentId = document.Id,
            SharedWithUserId = targetUserId,
            SharedWithRoleId = targetRoleId,
            Permission = model.Permission
        };

        context.DocumentShares.Add(share);
        await context.SaveChangesAsync();

        logger.LogInformation("Document with ID: '{DocumentId}' was shared by user: '{UserId}'.", id, userId);

        return RedirectToAction(nameof(ManageShares), new { id });
    }

    /// <summary>
    /// POST: Remove a share
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveShare([Required][FromRoute] Guid id)
    {
        var share = await context.DocumentShares
            .Include(s => s.Document)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (share == null)
        {
            return NotFound("Share not found.");
        }

        var userId = userManager.GetUserId(User);
        var isOwner = share.Document.UserId == userId;
        var canManage = isOwner || (await authorizationService.AuthorizeAsync(User, AppPermissionNames.CanManageAnyShare)).Succeeded;

        if (!canManage)
        {
            return Forbid();
        }

        context.DocumentShares.Remove(share);
        await context.SaveChangesAsync();

        logger.LogInformation("Share with ID: '{ShareId}' was removed by user: '{UserId}'.", id, userId);

        return RedirectToAction(nameof(ManageShares), new { id = share.DocumentId });
    }

    /// <summary>
    /// GET: View documents shared with me
    /// </summary>
    [HttpGet]
    [Authorize]
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Home",
        CascadedLinksIcon = "share-2",
        CascadedLinksOrder = 3,
        LinkText = "Shared with me",
        LinkOrder = 3)]
    public async Task<IActionResult> SharedWithMe()
    {
        var userId = userManager.GetUserId(User);
        var user = await userManager.FindByIdAsync(userId!);

        if (user == null)
        {
            return NotFound("User not found.");
        }

        // Get user's roles
        var userRoles = await userManager.GetRolesAsync(user);
        var userRoleIds = await context.Roles
            .Where(r => userRoles.Contains(r.Name!))
            .Select(r => r.Id)
            .ToListAsync();

        // Get documents shared directly with user or with user's roles
        var shares = await context.DocumentShares
            .Include(s => s.Document)
                .ThenInclude(d => d.User)
            .Where(s => s.SharedWithUserId == userId || (s.SharedWithRoleId != null && userRoleIds.Contains(s.SharedWithRoleId)))
            .OrderByDescending(s => s.CreationTime)
            .ToListAsync();

        // Get role names
        var roleIds = shares.Where(s => s.SharedWithRoleId != null).Select(s => s.SharedWithRoleId).Distinct().ToList();
        var roles = await context.Roles
            .Where(r => roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.Name!);

        var model = new SharedWithMeViewModel("Shared with Me")
        {
            Shares = shares,
            RoleNames = roles
        };

        return this.StackView(model);
    }

    [RenderInNavBar(
        NavGroupName = "Self Host",
        NavGroupOrder = 10,
        CascadedLinksGroupName = "Deployment",
        CascadedLinksIcon = "server",
        CascadedLinksOrder = 1,
        LinkText = "Self host a new server",
        LinkOrder = 1
    )]
    public IActionResult SelfHost()
    {
        return this.StackView(new SelfHostViewModel("Self host a new server"));
    }
}
