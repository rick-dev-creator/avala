using Avala.Components.Status;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IAvaViewModel
{
    Mood Mood { get; }

    string Kanji { get; }

    string Moment { get; }

    string Line { get; }

    bool IsOpen { get; }

    bool IsWaiting { get; }

    bool IsLingering { get; }

    IRelayCommand FoldCommand { get; }

    IRelayCommand ToggleCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class AvaViewModel : IAvaViewModel
{
    private StatusKind? shown;

    public AvaViewModel()
    {
        Kanji = AvaPhrases.Kanji(Mood.Focused);
        Moment = AvaPhrases.Moment(StatusKind.Working);
        Line = AvaPhrases.Line(StatusKind.Working);
    }

    [ObservableProperty]
    public partial Mood Mood { get; private set; }

    [ObservableProperty]
    public partial string Kanji { get; private set; }

    [ObservableProperty]
    public partial string Moment { get; private set; }

    [ObservableProperty]
    public partial string Line { get; private set; }

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial bool IsWaiting { get; private set; }

    [ObservableProperty]
    public partial bool IsLingering { get; private set; }

    public static AvaViewModel Of(StatusKind status)
    {
        var ava = new AvaViewModel();
        ava.Show(status);

        return ava;
    }

    public void Show(StatusKind status)
    {
        if (shown == status)
        {
            return;
        }

        var first = shown is null;
        shown = status;
        Mood = Moods.Of(status);
        Kanji = AvaPhrases.Kanji(Mood);
        Moment = AvaPhrases.Moment(status);
        Line = AvaPhrases.Line(status);
        IsWaiting = status is StatusKind.NeedsYou or StatusKind.Held;
        IsLingering = !first && status is StatusKind.Failed or StatusKind.ReadyForReview or StatusKind.Done;
        IsOpen = IsWaiting || IsLingering;
    }

    [RelayCommand]
    private void Fold()
    {
        if (!IsWaiting)
        {
            Close();
        }
    }

    [RelayCommand]
    private void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            IsOpen = true;
        }
    }

    private void Close()
    {
        IsLingering = false;
        IsOpen = false;
    }
}
