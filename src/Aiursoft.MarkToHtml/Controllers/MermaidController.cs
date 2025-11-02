using Aiursoft.MarkToHtml.Configuration;
using Aiursoft.MarkToHtml.Models.MermaidViewModels;
using Aiursoft.MarkToHtml.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Aiursoft.MarkToHtml.Controllers;

public class MermaidController(
    IOptions<AppSettings> appSettings,
    ILogger<MermaidController> logger) : Controller
{

    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Home",
        CascadedLinksOrder = 3,
        LinkText = "Mermaid",
        LinkOrder = 3
    )]
    public IActionResult Mermaid()
    {
        if (!User.Identity!.IsAuthenticated && !appSettings.Value.AllowAnonymousUsage)
        {
            logger.LogWarning("Anonymous user trying to access the mermaid page. But it is not allowed.");
            return Challenge();
        }
        return this.StackView(new MermaidViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Mermaid(MermaidViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return this.StackView(model);
        }

        // For mermaid, we don't need server-side conversion. We wrap input in a container for client-side rendering.
        var sanitized = $"<pre class=\"mermaid\">{System.Net.WebUtility.HtmlEncode(model.InputMermaid)}</pre>";
        model.OutputHtml = sanitized;
        return this.StackView(model);
    }
}


