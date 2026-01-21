using Aiursoft.UiStack.Layout;
using System.ComponentModel.DataAnnotations;

namespace Aiursoft.MarkToHtml.Models.ContractViewModels;

public class ContractViewModel : UiStackLayoutViewModel
{
    [Obsolete("Framework only")]
    public ContractViewModel()
    {
        PageTitle = "Contract";
    }

    public ContractViewModel(string title)
    {
        PageTitle = $"{title} - Contract";
        Title = title;
    }

    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;

    [Display(Name = "Contract Number")]
    public string ContractNumber { get; set; } = "AIUR-" + DateTime.Now.ToString("yyyyMMdd") + "-001";
    
    [Display(Name = "Sign Date")]
    public string SignDate { get; set; } = DateTime.Now.ToString("yyyy年 MM 月 dd 日");
    
    [Display(Name = "Sign Location")]
    public string SignLocation { get; set; } = "江苏省苏州市";

    [Display(Name = "Party A Name")]
    public string PartyAName { get; set; } = "上海乐府学堂网络科技有限公司";
    
    [Display(Name = "Party A Address")]
    public string PartyAAddress { get; set; } = string.Empty;
    
    [Display(Name = "Party A Contact")]
    public string PartyAContact { get; set; } = string.Empty;

    [Display(Name = "Party B Name")]
    public string PartyBName { get; set; } = "苏州艾软科技有限公司";
    
    [Display(Name = "Party B Address")]
    public string PartyBAddress { get; set; } = string.Empty;
    
    [Display(Name = "Party B Contact")]
    public string PartyBContact { get; set; } = "Anduin";
    
    public bool ShowPreview { get; set; }
}
