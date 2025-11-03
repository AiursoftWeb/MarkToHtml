using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Models.MermaidViewModel;

public class HistoryViewModel : UiStackLayoutViewModel
{
    public HistoryViewModel()
    {
        PageTitle = "My Mermaid History";
    }

    public IEnumerable<Document> MyDocuments { get; set; } = new List<Document>();
}
