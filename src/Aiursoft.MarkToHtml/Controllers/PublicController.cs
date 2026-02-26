using System.ComponentModel.DataAnnotations;
using Aiursoft.MarkToHtml.Models.PublicViewModels;
using Aiursoft.MarkToHtml.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Controllers;

/// <summary>
/// Controller for shared documents.
/// This controller allows users to view documents that have been made public or shared with them.
/// </summary>
[Route("share/{id:guid}")]
public class PublicController(
    ILogger<PublicController> logger,
    TemplateDbContext context,
    MarkToHtmlService mtohService) : Controller
{
    /// <summary>
    /// View a shared document.
    /// </summary>
    /// <param name="id">The ID of the document to view.</param>
    /// <returns>The view of the document.</returns>
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

        // Permission check
        var hasAccess = await HasReadAccess(document);
        if (!hasAccess)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                logger.LogWarning("User '{UserId}' attempted to access document '{DocumentId}' without permission", 
                    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, id);
                return Forbid();
            }
            return Challenge();
        }

        logger.LogInformation(
            "Document with ID: '{DocumentId}' accessed. Public: {IsPublic}",
            document.Id, document.IsPublic);

        var outputHtml = mtohService.ConvertMarkdownToHtml(document.Content ?? string.Empty);

        var model = new PublicDocumentViewModel(document.Title ?? "Untitled Document")
        {
            DocumentTitle = document.Title ?? "Untitled Document",
            Content = outputHtml,
            MarkdownContent = document.Content ?? string.Empty,
            AuthorName = document.User.UserName ?? "Unknown Author",
            CreationTime = document.CreationTime,
            CanEdit = await HasEditAccess(document)
        };

        ViewBag.DocumentId = id;
        return this.StackView(model);
    }

    /// <summary>
    /// Print a shared document.
    /// </summary>
    /// <param name="id">The ID of the document to print.</param>
    /// <returns>A clean view for printing.</returns>
    [HttpGet("print")]
    public async Task<IActionResult> Print([Required][FromRoute] Guid id)
    {
        logger.LogTrace("Attempting to print document with ID: '{Id}'", id);

        var document = await context.MarkdownDocuments
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            logger.LogWarning("Document with ID: '{Id}' was not found.", id);
            return NotFound("The document was not found.");
        }

        // Permission check
        var hasAccess = await HasReadAccess(document);
        if (!hasAccess)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                logger.LogWarning("User '{UserId}' attempted to print document '{DocumentId}' without permission",
                    User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, id);
                return Forbid();
            }
            return Challenge();
        }

        logger.LogInformation(
            "Document with ID: '{DocumentId}' printing accessed.",
            document.Id);

        var outputHtml = mtohService.ConvertMarkdownToHtml(document.Content ?? string.Empty);

        var model = new PublicDocumentViewModel(document.Title ?? "Untitled Document")
        {
            DocumentTitle = document.Title ?? "Untitled Document",
            Content = outputHtml,
            MarkdownContent = document.Content ?? string.Empty,
            AuthorName = document.User.UserName ?? "Unknown Author",
            CreationTime = document.CreationTime,
            CanEdit = await HasEditAccess(document)
        };

        return View(model);
    }

    /// <summary>
    /// View the raw Markdown content of a shared document.
    /// </summary>
    /// <param name="id">The ID of the document to view.</param>
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

        // Permission check
        var hasAccess = await HasReadAccess(document);
        if (!hasAccess)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return Forbid();
            }
            return Challenge();
        }

        logger.LogInformation(
            "Raw markdown for document with ID: '{DocumentId}' accessed.",
            document.Id);

        // Return raw markdown as plain text
        return Content(document.Content ?? string.Empty, "text/plain; charset=utf-8");
    }

    private async Task<bool> HasReadAccess(MarkdownDocument document)
    {
        // 1. Is public
        if (document.IsPublic)
        {
            return true;
        }

        // 2. Is owner
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId != null && document.UserId == userId)
        {
            return true;
        }

        // 3. Is shared with the user (directly or via role)
        if (userId != null)
        {
            var userRoles = await context.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            var hasSharedAccess = await context.DocumentShares
                .AnyAsync(s => s.DocumentId == document.Id &&
                              (s.SharedWithUserId == userId ||
                               (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));

            if (hasSharedAccess)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<bool> HasEditAccess(MarkdownDocument document)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
        {
            return false;
        }

        // 1. Is owner
        if (document.UserId == userId)
        {
            return true;
        }

        // 2. Is shared with the user with Editable permission
        var userRoles = await context.UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();

        var hasEditAccess = await context.DocumentShares
            .AnyAsync(s => s.DocumentId == document.Id &&
                           s.Permission == SharePermission.Editable &&
                           (s.SharedWithUserId == userId ||
                            (s.SharedWithRoleId != null && userRoles.Contains(s.SharedWithRoleId))));

        return hasEditAccess;
    }
}
