using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Components.Status;

[INotifyPropertyChanged]
public sealed partial class StatusDotViewModel : IStatusDotViewModel
{
    public StatusDotViewModel(StatusKind kind) => Kind = kind;

    [ObservableProperty]
    public partial StatusKind Kind { get; set; }
}
