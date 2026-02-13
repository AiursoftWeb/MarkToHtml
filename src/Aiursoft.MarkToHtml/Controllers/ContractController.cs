using Aiursoft.MarkToHtml.Configuration;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Models.ContractViewModels;
using Aiursoft.MarkToHtml.Services;
using Aiursoft.MarkToHtml.Services.FileStorage;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Aiursoft.MarkToHtml.Controllers;

[Route("contract/{id:guid}")]
public class ContractController(
    UserManager<User> userManager,
    TemplateDbContext context,
    MarkToHtmlService mtohService,
    GlobalSettingsService globalSettingsService,
    StorageService storageService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Fill([Required][FromRoute] Guid id)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        var hasAccess = await HasReadAccess(document);
        if (!hasAccess)
        {
            return Challenge();
        }

        var model = new ContractViewModel(document.Title ?? "Untitled Document")
        {
            DocumentId = document.Id,
            ShowPreview = false
        };

        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Fill([Required][FromRoute] Guid id, ContractViewModel model)
    {
        var document = await context.MarkdownDocuments
            .FirstOrDefaultAsync(d => d.Id == id);

        if (document == null)
        {
            return NotFound("The document was not found.");
        }

        var hasAccess = await HasReadAccess(document);
        if (!hasAccess)
        {
            return Challenge();
        }

        model.Title = document.Title ?? "Untitled Document";
        model.PageTitle = $"{model.Title} - Contract";

        if (!ModelState.IsValid)
        {
            model.ShowPreview = false;
            return this.StackView(model);
        }

        var logoPath = await globalSettingsService.GetSettingValueAsync(SettingsMap.ProjectLogo);
        if (!string.IsNullOrWhiteSpace(logoPath))
        {
            model.LogoUrl = storageService.RelativePathToInternetUrl(logoPath, HttpContext);
        }
        else
        {
            model.LogoUrl = "/logo.svg";
        }
        model.CompanyAddress = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyAddress);
        model.CompanyPhone = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyPhone);
        model.CompanyEmail = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyEmail);
        model.CompanyPostcode = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyPostcode);

        model.ContentHtml = mtohService.ConvertMarkdownToHtml(document.Content ?? string.Empty);
        model.ShowPreview = true;
        return this.StackView(model, nameof(Fill));
    }

    private async Task<bool> HasReadAccess(MarkdownDocument document)
    {
        if (document.IsPublic)
        {
            return true;
        }

        var userId = userManager.GetUserId(User);
        if (userId != null && document.UserId == userId)
        {
            return true;
        }

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
}
