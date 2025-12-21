using System.ComponentModel.DataAnnotations;
using Aiursoft.MarkToHtml.Entities;

namespace Aiursoft.MarkToHtml.Models.HomeViewModels;

public class AddShareViewModel
{
    public string? TargetUserId { get; set; }
    
    public string? TargetRoleId { get; set; }
    
    [Required]
    public SharePermission Permission { get; set; }
}
