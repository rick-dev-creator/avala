using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Shell;

[INotifyPropertyChanged]
internal sealed partial class ShellViewModel
{
    public ShellViewModel(IEnumerable<IPage> pages)
    {
        Pages = [.. pages];
        SelectedPage = Pages.Count > 0 ? Pages[0] : null;
    }

    public IReadOnlyList<IPage> Pages { get; }

    [ObservableProperty]
    public partial IPage? SelectedPage { get; set; }
}
