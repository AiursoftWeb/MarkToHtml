using Microsoft.AspNetCore.Mvc;
using Aiursoft.MarkToHtml.Models.HomeViewModels;
using Aiursoft.MarkToHtml.Services;


namespace Aiursoft.MarkToHtml.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return this.StackView(new HomeViewModel());
    }
}
