using Aiursoft.UiStack.Layout;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Models.AdminViewModels;

public class UserDocumentsViewModel : UiStackLayoutViewModel
{
    public UserDocumentsViewModel()
    {
        PageTitle = "User Documents";
    }

    public required User User { get; set; }
    public required List<MarkdownDocument> UserDocuments { get; set; }
    public string? SearchQuery { get; set; }
}
