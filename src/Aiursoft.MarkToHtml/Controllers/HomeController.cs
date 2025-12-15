using System.ComponentModel.DataAnnotations;
using Aiursoft.CSTools.Tools;
using Aiursoft.MarkToHtml.Models.HomeViewModels;
using Aiursoft.MarkToHtml.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Aiursoft.MarkToHtml.Configuration;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.WebTools.Attributes;


namespace Aiursoft.MarkToHtml.Controllers;

[LimitPerMin]
public class HomeController(
    IOptions<AppSettings> appSettings,
    ILogger<HomeController> logger,
    UserManager<User> userManager,
    TemplateDbContext context,
    MarkToHtmlService mtohService) : Controller
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
        if (!User.Identity!.IsAuthenticated && !appSettings.Value.AllowAnonymousUsage)
        {
            logger.LogWarning("Anonymous user trying to access the home page. But it is not allowed.");
            return Challenge();
        }
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
                .FirstOrDefaultAsync(d => d.Id == model.DocumentId && d.UserId == userId);
            var isExistingDocument = documentInDb != null;
            if (documentInDb != null)
            {
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
        var document = await context.MarkdownDocuments.FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

        if (document == null)
        {
            return NotFound("The document was not found or you do not have permission to edit it.");
        }

        var publicLink = document.PublicId.HasValue
            ? Url.Action(nameof(PublicController.View), "Public", new { publicId = document.PublicId }, Request.Scheme)
            : null;

        var model = new IndexViewModel(document.Title ?? "Empty Document")
        {
            DocumentId = document.Id,
            Title = document.Title,
            InputMarkdown = document.Content ?? string.Empty,
            OutputHtml = mtohService.ConvertMarkdownToHtml(document.Content ?? string.Empty),
            IsEditing = true,
            SavedSuccessfully = saved ?? false,
            PublicId = document.PublicId,
            PublicLink = publicLink
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
    /// Make a document public by generating a PublicId.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MakePublic([Required][FromRoute] Guid id)
    {
        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

        if (document == null)
        {
            return NotFound("The document was not found or you do not have permission to modify it.");
        }

        if (!document.PublicId.HasValue)
        {
            document.PublicId = Guid.NewGuid();
            await context.SaveChangesAsync();
            logger.LogInformation("Document with ID: '{DocumentId}' was made public with PublicId: '{PublicId}' by user: '{UserId}'.",
                id, document.PublicId, userId);
        }

        return Ok(new { publicId = document.PublicId });
    }

    /// <summary>
    /// Make a document private by removing its PublicId.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MakePrivate([Required][FromRoute] Guid id)
    {
        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

        if (document == null)
        {
            return NotFound("The document was not found or you do not have permission to modify it.");
        }

        if (document.PublicId.HasValue)
        {
            var publicId = document.PublicId;
            document.PublicId = null;
            await context.SaveChangesAsync();
            logger.LogInformation("Document with ID: '{DocumentId}' was made private (removed PublicId: '{PublicId}') by user: '{UserId}'.",
                id, publicId, userId);
        }

        return Ok();
    }
}
