using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Models.MarkdownViewModel;

public class HistoryViewModel : UiStackLayoutViewModel
{
    public HistoryViewModel()
    {
        PageTitle = "My Documents History";
    }

    public IEnumerable<Document> MyDocuments { get; set; } = new List<Document>();
}
