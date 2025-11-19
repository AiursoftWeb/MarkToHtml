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
[ApiExplorerSettings(IgnoreApi = true)]
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
            AuthorName = document.User?.UserName ?? "Unknown Author",
            CreationTime = document.CreationTime
        };

        return this.StackView(model);
    }
}
