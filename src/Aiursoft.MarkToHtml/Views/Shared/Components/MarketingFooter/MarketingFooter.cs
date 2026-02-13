using Aiursoft.MarkToHtml.Configuration;
using Microsoft.AspNetCore.Mvc;
using Aiursoft.MarkToHtml.Services;
using Aiursoft.MarkToHtml.Services.FileStorage;

namespace Aiursoft.MarkToHtml.Views.Shared.Components.MarketingFooter;

public class MarketingFooter(
    GlobalSettingsService globalSettingsService) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(MarketingFooterViewModel? model = null)
    {
        model ??= new MarketingFooterViewModel();
        model.BrandName = await globalSettingsService.GetSettingValueAsync(SettingsMap.BrandName);
        model.BrandHomeUrl = await globalSettingsService.GetSettingValueAsync(SettingsMap.BrandHomeUrl);
        model.Icp = await globalSettingsService.GetSettingValueAsync(SettingsMap.Icp);
        model.LogoUrl = await globalSettingsService.GetLogoUrlAsync();
        
        return View(model);
    }
}
