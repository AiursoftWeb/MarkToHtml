using Aiursoft.MarkToHtml.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.MarkToHtml.Models.UsersViewModels;

public class DeleteViewModel : UiStackLayoutViewModel
{
    public DeleteViewModel()
    {
        PageTitle = "Delete User";
    }

    public required User User { get; set; }
}
