using Aiursoft.MarkToHtml.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.MarkToHtml.Models.HomeViewModels;

public class SearchViewModel : UiStackLayoutViewModel
{
    public SearchViewModel()
    {
        PageTitle = "Search";
    }

    public string? Query { get; set; }
    public IEnumerable<MarkdownDocument> Results { get; set; } = [];
    public string? CurrentUserId { get; set; }
    public bool UsedAiSearch { get; set; }
    public bool RateLimited { get; set; }
}
