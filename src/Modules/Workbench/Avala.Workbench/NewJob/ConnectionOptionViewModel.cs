using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.NewJob;

internal interface IConnectionOptionViewModel
{
    string Name { get; }

    string Reading { get; }

    bool IsNearLimit { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ConnectionOptionViewModel(string name) : IConnectionOptionViewModel
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial string Reading { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsNearLimit { get; private set; }

    public void Show(string reading, bool nearLimit)
    {
        Reading = reading;
        IsNearLimit = nearLimit;
    }
}
