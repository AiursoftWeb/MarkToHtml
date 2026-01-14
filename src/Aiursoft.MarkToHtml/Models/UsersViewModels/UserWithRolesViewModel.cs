using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Models.UsersViewModels;

public class UserWithRolesViewModel
{
    public required User User { get; set; }
    public required IList<string> Roles { get; set; }
}