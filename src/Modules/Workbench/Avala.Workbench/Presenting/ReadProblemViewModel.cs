using Avala.Sdk;
using Avala.Workbench.Following;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Presenting;

internal interface IReadProblemViewModel
{
    bool IsShown { get; }

    string Text { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ReadProblemViewModel : IReadProblemViewModel
{
    public ReadProblemViewModel(LiveFeed feed) => feed.Presented += (_, _) => Show(feed.Failure);

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial string Text { get; private set; } = string.Empty;

    private void Show(Option<string> failure)
    {
        Text = failure.IsSome ? ReadProblemPhrases.Problem : string.Empty;
        IsShown = failure.IsSome;
    }
}

internal sealed class DesignReadProblemViewModel(bool failing) : IReadProblemViewModel
{
    public DesignReadProblemViewModel()
        : this(false)
    {
    }

    public bool IsShown => failing;

    public string Text => failing ? ReadProblemPhrases.Problem : string.Empty;
}

internal static class ReadProblemPhrases
{
    public const string Problem = "Avala could not read what this shows, so it may be out of date. It updates again once a read succeeds, and the details are in Avala's log.";
}
