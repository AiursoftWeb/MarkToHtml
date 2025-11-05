using System.Diagnostics;
using Aiursoft.MarkToHtml.Models.ErrorViewModels;
using Aiursoft.MarkToHtml.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.MarkToHtml.Controllers;

/// <summary>
/// This controller is used to show error pages.
/// </summary>
[LimitPerMin]
public class ErrorController : Controller
{
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return this.StackView(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [Route("Error/Unauthorized")]
    public IActionResult UnauthorizedPage([FromQuery]string returnUrl = "/")
    {
        if (!Url.IsLocalUrl(returnUrl))
        {
            returnUrl = "/";
        }

        return this.StackView(new UnauthorizedViewModel
        {
            ReturnUrl = returnUrl
        }, viewName: "Unauthorized");
    }
}
