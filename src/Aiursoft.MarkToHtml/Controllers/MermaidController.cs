using System.ComponentModel.DataAnnotations;
using Aiursoft.CSTools.Tools;
using Aiursoft.MarkToHtml.Configuration;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Models.MermaidViewModels;
using Aiursoft.MarkToHtml.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Aiursoft.MarkToHtml.Controllers;

public class MermaidController(
    IOptions<AppSettings> appSettings,
    ILogger<MermaidController> logger,
    UserManager<User> userManager,
    TemplateDbContext context) : Controller
{

    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Home",
        CascadedLinksOrder = 3,
        LinkText = "Mermaid",
        LinkOrder = 3
    )]
    public IActionResult Mermaid()
    {
        if (!User.Identity!.IsAuthenticated && !appSettings.Value.AllowAnonymousUsage)
        {
            logger.LogWarning("Anonymous user trying to access the mermaid page. But it is not allowed.");
            return Challenge();
        }
        return this.StackView(new MermaidViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Mermaid(MermaidViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return this.StackView(model);
        }

        var userId = userManager.GetUserId(User);
        if (User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(userId))
        {
            // Save to DB and redirect to edit
            logger.LogTrace("Authenticated user submitted a mermaid with ID: '{Id}'. Save it to the database.",
                model.DocumentId);
            var documentInDb = await context.MarkdownDocuments
                .FirstOrDefaultAsync(d => d.Id == model.DocumentId && d.UserId == userId && d.DocumentType == DocumentType.Mermaid);
            var isExistingDocument = documentInDb != null;
            if (documentInDb != null)
            {
                logger.LogInformation("Updating the mermaid document with ID: '{Id}'.", model.DocumentId);
                documentInDb.Content = model.InputMermaid.SafeSubstring(65535);
                documentInDb.Title = model.Title;
            }
            else
            {
                logger.LogInformation("Creating a new mermaid document with ID: '{Id}'.", model.DocumentId);
                model.DocumentId = Guid.NewGuid();
                var newDocument = new Document
                {
                    Id = model.DocumentId,
                    Content = model.InputMermaid.SafeSubstring(65535),
                    Title = model.InputMermaid.SafeSubstring(40),
                    UserId = userId,
                    DocumentType = DocumentType.Mermaid
                };
                context.MarkdownDocuments.Add(newDocument);
            }

            await context.SaveChangesAsync();
            return RedirectToAction(nameof(Edit), new { id = model.DocumentId, saved = isExistingDocument });
        }
        else
        {
            // Anonymous: just return wrapped content for client-side rendering
            var sanitized = $"<pre class=\"mermaid\">{System.Net.WebUtility.HtmlEncode(model.InputMermaid)}</pre>";
            model.OutputHtml = sanitized;
            return this.StackView(model);
        }
    }

    [Authorize]
    public async Task<IActionResult> Edit([Required][FromRoute] Guid id, [FromQuery] bool? saved = false)
    {
        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && d.DocumentType == DocumentType.Mermaid);

        if (document == null)
        {
            return NotFound("The document was not found or you do not have permission to edit it.");
        }

        var model = new MermaidViewModel
        {
            DocumentId = document.Id,
            Title = document.Title,
            InputMermaid = document.Content ?? string.Empty,
            // For mermaid, OutputHtml is a pre block for client-side render
            OutputHtml = $"<pre class=\"mermaid\">{System.Net.WebUtility.HtmlEncode(document.Content ?? string.Empty)}</pre>",
            IsEditing = true,
            SavedSuccessfully = saved ?? false
        };

        return this.StackView(model: model, viewName: nameof(Mermaid));
    }

    [Authorize]
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Home",
        CascadedLinksIcon = "history",
        CascadedLinksOrder = 4,
        LinkText = "My mermaid charts",
        LinkOrder = 4)]
    public async Task<IActionResult> History()
    {
        var userId = userManager.GetUserId(User);
        var documents = await context.MarkdownDocuments
            .Where(d => d.UserId == userId && d.DocumentType == DocumentType.Mermaid)
            .OrderByDescending(d => d.CreationTime)
            .ToListAsync();

        var model = new HistoryViewModel
        {
            MyDocuments = documents
        };
        return this.StackView(model);
    }

    [Authorize]
    public async Task<IActionResult> Delete(Guid? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && d.DocumentType == DocumentType.Mermaid);

        if (document == null)
        {
            return NotFound();
        }

        return this.StackView(new DeleteViewModel
        {
            Document = document
        });
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        var userId = userManager.GetUserId(User);
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId && d.DocumentType == DocumentType.Mermaid);

        if (document == null)
        {
            return NotFound();
        }

        context.MarkdownDocuments.Remove(document);
        await context.SaveChangesAsync();

        logger.LogInformation("Mermaid document with ID: '{Id}' was deleted by user: '{UserId}'.", id, userId);

        return RedirectToAction(nameof(History));
    }
}
