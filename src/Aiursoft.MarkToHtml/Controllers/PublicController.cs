using System.ComponentModel.DataAnnotations;
using Aiursoft.MarkToHtml.Models.PublicViewModels;
using Aiursoft.MarkToHtml.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Controllers;

/// <summary>
/// Controller for publicly shared documents.
/// This controller allows anonymous users to view documents that have been made public.
/// </summary>
[Route("view/{id:guid}")]
public class PublicController(
    ILogger<PublicController> logger,
    TemplateDbContext context,
    MarkToHtmlService mtohService) : Controller
{
    /// <summary>
    /// View a document by its ID.
    /// </summary>
    /// <param name="id">The document ID.</param>
    /// <returns>The document view.</returns>
    [HttpGet]
    public async Task<IActionResult> View([Required][FromRoute] Guid id)
    {
        logger.LogTrace("Attempting to view document with ID: '{Id}'", id);

        var document = await context.MarkdownDocuments
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            logger.LogWarning("Document with ID: '{Id}' was not found.", id);
            return NotFound("The document was not found.");
        }

        bool hasAccess = false;

        // 1. Check if public
        if (document.AllowAnonymousView)
        {
            hasAccess = true;
            logger.LogInformation("Document '{Id}' is public. Access granted.", id);
        }
        else if (User.Identity?.IsAuthenticated == true)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                // 2. Check if owner
                if (document.UserId == userId)
                {
                    hasAccess = true;
                    logger.LogInformation("User '{UserId}' is the owner of document '{Id}'. Access granted.", userId, id);
                }
                else
                {
                    // 3. Check if shared
                    var userRoles = await context.UserRoles
                        .Where(ur => ur.UserId == userId)
                        .Select(ur => ur.RoleId)
                        .ToListAsync();

                    hasAccess = await context.DocumentShares
                        .AnyAsync(s => s.DocumentId == id &&
                                      (s.SharedWithUserId == userId ||
                                       (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));

                    if (hasAccess)
                    {
                        logger.LogInformation("User '{UserId}' has shared access to document '{Id}'. Access granted.", userId, id);
                    }
                }
            }
        }

        if (!hasAccess)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                logger.LogWarning("User attempted to access document '{Id}' without permission", id);
                return Forbid();
            }
            else
            {
                logger.LogWarning("Anonymous user attempted to access private document '{Id}'", id);
                return Challenge();
            }
        }

        var outputHtml = mtohService.ConvertMarkdownToHtml(document.Content ?? string.Empty);

        var model = new PublicDocumentViewModel(document.Title ?? "Untitled Document")
        {
            DocumentTitle = document.Title ?? "Untitled Document",
            Content = outputHtml,
            MarkdownContent = document.Content ?? string.Empty,
            AuthorName = document.User.UserName ?? "Unknown Author",
            CreationTime = document.CreationTime
        };

        ViewBag.DocumentId = id;
        return this.StackView(model);
    }

    /// <summary>
    /// View the raw Markdown content of a document.
    /// </summary>
    /// <param name="id">The document ID.</param>
    /// <returns>The raw Markdown content of the document.</returns>
    [HttpGet("raw")]
    public async Task<IActionResult> Raw([Required][FromRoute] Guid id)
    {
        logger.LogTrace("Attempting to view raw markdown for document with ID: '{Id}'", id);

        var document = await context.MarkdownDocuments
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            logger.LogWarning("Document with ID: '{Id}' was not found.", id);
            return NotFound("The document was not found.");
        }

        bool hasAccess = false;

        // 1. Check if public
        if (document.AllowAnonymousView)
        {
            hasAccess = true;
        }
        else if (User.Identity?.IsAuthenticated == true)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                // 2. Check if owner
                if (document.UserId == userId)
                {
                    hasAccess = true;
                }
                else
                {
                    // 3. Check if shared
                    var userRoles = await context.UserRoles
                        .Where(ur => ur.UserId == userId)
                        .Select(ur => ur.RoleId)
                        .ToListAsync();

                    hasAccess = await context.DocumentShares
                        .AnyAsync(s => s.DocumentId == id &&
                                      (s.SharedWithUserId == userId ||
                                       (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));
                }
            }
        }

        if (!hasAccess)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return Forbid();
            }
            else
            {
                return Challenge();
            }
        }

        // Return raw markdown as plain text
        return Content(document.Content ?? string.Empty, "text/plain; charset=utf-8");
    }
}
