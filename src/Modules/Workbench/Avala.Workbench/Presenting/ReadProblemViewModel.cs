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
        Text = failure.Match(ReadProblemPhrases.Problem, () => string.Empty);
        IsShown = failure.IsSome;
    }
}

internal sealed class DesignReadProblemViewModel(string reason) : IReadProblemViewModel
{
    public DesignReadProblemViewModel()
        : this(string.Empty)
    {
    }

    public bool IsShown => reason.Length > 0;

    public string Text => IsShown ? ReadProblemPhrases.Problem(reason) : string.Empty;
}

internal static class ReadProblemPhrases
{
    public static string Problem(string reason) => $"Could not read what this shows, so it may be out of date. It updates again once a read succeeds. {reason}";
}
