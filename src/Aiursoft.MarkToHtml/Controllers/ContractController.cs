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
    GlobalSettingsService globalSettingsService) : Controller
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

        await PopulateCompanySettings(model);
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

        await PopulateCompanySettings(model);
        if (!ModelState.IsValid)
        {
            model.ShowPreview = false;
            return this.StackView(model);
        }

        model.ContentHtml = mtohService.ConvertMarkdownToHtml(document.Content ?? string.Empty);
        model.ShowPreview = true;
        return this.StackView(model, nameof(Fill));
    }

    private async Task PopulateCompanySettings(ContractViewModel model)
    {
        model.LogoUrl = await globalSettingsService.GetLogoUrlAsync();
        model.CompanyAddress = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyAddress);
        model.CompanyPhone = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyPhone);
        model.CompanyEmail = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyEmail);
        model.CompanyPostcode = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyPostcode);
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
