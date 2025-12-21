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
[Route("public/{publicId}")]
public class PublicController(
    ILogger<PublicController> logger,
    TemplateDbContext context,
    MarkToHtmlService mtohService) : Controller
{
    /// <summary>
    /// View a publicly shared document.
    /// </summary>
    /// <param name="publicId">The public ID of the document to view.</param>
    /// <returns>The public view of the document.</returns>
    [HttpGet]
    public async Task<IActionResult> View([Required][FromRoute] Guid publicId)
    {
        logger.LogTrace("Attempting to view public document with ID: '{PublicId}'", publicId);

        var document = await context.MarkdownDocuments
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.PublicId == publicId);

        if (document == null)
        {
            logger.LogWarning("Public document with ID: '{PublicId}' was not found.", publicId);
            return NotFound("The public document was not found.");
        }

        logger.LogInformation(
            "Public document with ID: '{PublicId}' accessed by anonymous user. Document ID: '{DocumentId}'",
            publicId, document.Id);

        var outputHtml = mtohService.ConvertMarkdownToHtml(document.Content ?? string.Empty);

        var model = new PublicDocumentViewModel(document.Title ?? "Untitled Document")
        {
            DocumentTitle = document.Title ?? "Untitled Document",
            Content = outputHtml,
            MarkdownContent = document.Content ?? string.Empty,
            AuthorName = document.User.UserName ?? "Unknown Author",
            CreationTime = document.CreationTime
        };

        ViewBag.PublicId = publicId;
        return this.StackView(model);
    }

    /// <summary>
    /// View the raw Markdown content of a publicly shared document.
    /// </summary>
    /// <param name="publicId">The public ID of the document to view.</param>
    /// <returns>The raw Markdown content of the document.</returns>
    [HttpGet("raw")]
    public async Task<IActionResult> Raw([Required][FromRoute] Guid publicId)
    {
        logger.LogTrace("Attempting to view raw markdown for public document with ID: '{PublicId}'", publicId);

        var document = await context.MarkdownDocuments
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.PublicId == publicId);

        if (document == null)
        {
            logger.LogWarning("Public document with ID: '{PublicId}' was not found.", publicId);
            return NotFound("The public document was not found.");
        }

        logger.LogInformation(
            "Raw markdown for public document with ID: '{PublicId}' accessed by user. Document ID: '{DocumentId}'",
            publicId, document.Id);

        // Return raw markdown as plain text
        return Content(document.Content ?? string.Empty, "text/plain; charset=utf-8");
    }

    /// <summary>
    /// View a document by its ID (requires authentication and proper permissions).
    /// User must be the owner or have the document shared with them.
    /// </summary>
    /// <param name="id">The document ID.</param>
    /// <returns>The document view.</returns>
    [HttpGet("/view/{id:guid}")]
    public async Task<IActionResult> ViewById([Required][FromRoute] Guid id)
    {
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return Challenge();
        }

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        logger.LogTrace("User '{UserId}' attempting to view document with ID: '{DocumentId}'", userId, id);

        var document = await context.MarkdownDocuments
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            logger.LogWarning("Document with ID: '{DocumentId}' was not found.", id);
            return NotFound("The document was not found.");
        }

        // Check if user is the owner
        if (document.UserId == userId)
        {
            logger.LogInformation("Document owner '{UserId}' accessing document '{DocumentId}'", userId, id);
        }
        else
        {
            // Check if document is shared with the user (directly or via role)
            var user = await context.Users.FindAsync(userId);
            if (user == null)
            {
                return Unauthorized();
            }

            var userRoles = await context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            var hasAccess = await context.DocumentShares
                .AnyAsync(s => s.DocumentId == id &&
                              (s.SharedWithUserId == userId ||
                               (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));

            if (!hasAccess)
            {
                logger.LogWarning("User '{UserId}' attempted to access document '{DocumentId}' without permission", userId, id);
                return Forbid();
            }

            logger.LogInformation("User '{UserId}' accessing shared document '{DocumentId}'", userId, id);
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
}
