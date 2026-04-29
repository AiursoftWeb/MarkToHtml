using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Models.AdminViewModels;

public class AllDocumentsViewModel : UiStackLayoutViewModel
{
    public AllDocumentsViewModel()
    {
        PageTitle = "All Documents";
    }

    public required List<MarkdownDocument> AllDocuments { get; set; }
    public string? SearchQuery { get; set; }
}
