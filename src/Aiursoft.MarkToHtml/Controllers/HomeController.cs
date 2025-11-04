using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Aiursoft.MarkToHtml.Configuration;
using Aiursoft.MarkToHtml.Entities;
using Aiursoft.WebTools.Attributes;


namespace Aiursoft.MarkToHtml.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return this.StackView(new HomeViewModel());
    }
}
