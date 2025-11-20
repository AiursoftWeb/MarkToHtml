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

        var model = new PublicDocumentViewModel
        {
            DocumentTitle = document.Title ?? "Untitled Document",
            Content = outputHtml,
            MarkdownContent = document.Content ?? string.Empty,
            AuthorName = document.User?.UserName ?? "Unknown Author",
            CreationTime = document.CreationTime
        };

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
}
