using Aiursoft.MarkToHtml.Models.DashboardViewModels;
using Aiursoft.MarkToHtml.Services;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.MarkToHtml.Controllers;

[Authorize]
[LimitPerMin]
public class DashboardController : Controller
{
    [RenderInNavBar(
        NavGroupName = "Dashboard",
        NavGroupOrder = 0,
        CascadedLinksGroupName = "Dashboard",
        CascadedLinksIcon = "layout-dashboard",
        CascadedLinksOrder = 0,
        LinkText = "Dashboard",
        LinkOrder = 0)]
    public IActionResult Index()
    {
        return this.StackView(new IndexViewModel());
    }
}
