using Aiursoft.UiStack.Layout;

namespace Aiursoft.MarkToHtml.Models.PermissionsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Permissions";
    }

    public required List<PermissionWithRoleCount> Permissions { get; init; }
}
