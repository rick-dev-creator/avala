using System.Globalization;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Conversation;

internal interface IRetryViewModel
{
    string Note { get; }

    bool IsLive { get; }

    bool GaveUp { get; }
}

[INotifyPropertyChanged]
internal sealed partial class RetryViewModel : IRetryViewModel, ITimelineItem, ISearchable
{
    public RetryViewModel(RetryEntry retry) => Show(retry);

    [ObservableProperty]
    public partial string Note { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsLive { get; private set; }

    [ObservableProperty]
    public partial bool GaveUp { get; private set; }

    public string SearchText => Note;

    [ObservableProperty]
    public partial FindMark Found { get; set; }

    public bool Unfold() => false;

    public bool IsShown => true;

    public void Update(ITimelineEntry entry)
    {
        if (entry is RetryEntry retry)
        {
            Show(retry);
        }
    }

    public static string Phrase(RetryEntry retry)
    {
        var why = retry.Reason.Length > 0 ? $" ({retry.Reason})" : string.Empty;
        var times = retry.Attempt == 1 ? "1 retry" : string.Create(CultureInfo.InvariantCulture, $"{retry.Attempt} retries");

        return retry switch
        {
            { Recovered: true } => $"The harness recovered after {times}{why}.",
            { GaveUp: true } => $"The harness stopped retrying after {times}{why}.",
            { Most: > 0 } => string.Create(CultureInfo.InvariantCulture, $"The harness is retrying · attempt {retry.Attempt} of {retry.Most}{why}"),
            _ => string.Create(CultureInfo.InvariantCulture, $"The harness is retrying · attempt {retry.Attempt}{why}"),
        };
    }

    private void Show(RetryEntry retry)
    {
        Note = Phrase(retry);
        IsLive = !retry.Recovered && !retry.GaveUp;
        GaveUp = retry.GaveUp;
    }
}
