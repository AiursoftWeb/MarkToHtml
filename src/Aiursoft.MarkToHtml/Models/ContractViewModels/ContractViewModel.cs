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
    public string SignLocation { get; set; } = "西京市";

    [Display(Name = "Party A Name")]
    public string PartyAName { get; set; } = "南都市巨富收购者股份有限公司";
    
    [Display(Name = "Party A Address")]
    public string PartyAAddress { get; set; } = "南都市幸福大道 1 号";
    
    [Display(Name = "Party A Contact")]
    public string PartyAContact { get; set; } = "王多鱼";

    [Display(Name = "Party B Name")]
    public string PartyBName { get; set; } = "西京市巨硬软件有限公司";
    
    [Display(Name = "Party B Address")]
    public string PartyBAddress { get; set; } = "西京市中关村大街 999 号";
    
    [Display(Name = "Party B Contact")]
    public string PartyBContact { get; set; } = "茨盖比";
    
    public bool ShowPreview { get; set; }

    public string LogoUrl { get; set; } = "/logo.svg";
    public string CompanyAddress { get; set; } = string.Empty;
    public string CompanyPhone { get; set; } = string.Empty;
    public string CompanyEmail { get; set; } = string.Empty;
    public string CompanyPostcode { get; set; } = string.Empty;
}
