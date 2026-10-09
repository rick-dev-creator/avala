using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Components.Status;

[INotifyPropertyChanged]
public sealed partial class StatusPillViewModel : IStatusPillViewModel
{
    private readonly StatusDotViewModel dot;

    public StatusPillViewModel(StatusKind kind, string text)
    {
        dot = new StatusDotViewModel(kind);
        Kind = kind;
        Text = text;
    }

    [ObservableProperty]
    public partial StatusKind Kind { get; set; }

    [ObservableProperty]
    public partial string Text { get; set; }

    public IStatusDotViewModel Dot => dot;

    partial void OnKindChanged(StatusKind value) => dot.Kind = value;
}
