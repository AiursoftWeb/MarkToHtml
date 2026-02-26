using Aiursoft.MarkToHtml.Configuration;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.MarkToHtml.Models.ContractViewModels;
using Aiursoft.MarkToHtml.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace Aiursoft.MarkToHtml.Controllers;

[Route("contract/{id:guid}")]
public class ContractController(
    TemplateDbContext context,
    MarkToHtmlService mtohService,
    GlobalSettingsService globalSettingsService,
    DocumentPermissionService permissionService) : Controller
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

        if (!await permissionService.CanReadAsync(User, document))
        {
            return Challenge();
        }

        var model = new ContractViewModel(document.Title ?? "Untitled Document")
        {
            DocumentId = document.Id,
            ShowPreview = false
        };

        await PopulateCompanySettings(model);
        return await this.StackViewAsync(model);
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

        if (!await permissionService.CanReadAsync(User, document))
        {
            return Challenge();
        }

        model.Title = document.Title ?? "Untitled Document";
        model.PageTitle = $"{model.Title} - Contract";

        await PopulateCompanySettings(model);
        if (!ModelState.IsValid)
        {
            model.ShowPreview = false;
            return await this.StackViewAsync(model);
        }

        model.ContentHtml = mtohService.ConvertMarkdownToHtml(document.Content ?? string.Empty);
        model.ShowPreview = true;
        return await this.StackViewAsync(model, nameof(Fill));
    }

    private async Task PopulateCompanySettings(ContractViewModel model)
    {
        model.LogoUrl = await globalSettingsService.GetLogoUrlAsync();
        model.CompanyAddress = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyAddress);
        model.CompanyPhone = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyPhone);
        model.CompanyEmail = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyEmail);
        model.CompanyPostcode = await globalSettingsService.GetSettingValueAsync(SettingsMap.CompanyPostcode);
    }
}
