using Aiursoft.MarkToHtml.Configuration;
using Microsoft.AspNetCore.Mvc;
using Aiursoft.MarkToHtml.Services;
using Aiursoft.MarkToHtml.Services.FileStorage;

namespace Aiursoft.MarkToHtml.Views.Shared.Components.MarketingNavbar;

public class MarketingNavbar(
    GlobalSettingsService globalSettingsService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(MarketingNavbarViewModel? model = null)
    {
        model ??= new MarketingNavbarViewModel();
        model.ProjectName = await globalSettingsService.GetSettingValueAsync(SettingsMap.ProjectName);
        model.LogoUrl = await globalSettingsService.GetLogoUrlAsync();
        return View(model);
    }
}
