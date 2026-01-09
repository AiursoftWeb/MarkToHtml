using Aiursoft.UiStack.Layout;

namespace Aiursoft.MarkToHtml.Models.HomeViewModels;

public class SelfHostViewModel : UiStackLayoutViewModel
{
    [Obsolete("This constructor is only used for framework!", true)]
    public SelfHostViewModel()
    {
    }

    public SelfHostViewModel(string title)
    {
        PageTitle = title;
    }
}
