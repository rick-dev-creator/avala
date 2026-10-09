using System.Globalization;
using Avala.Jobs.Contracts;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Decisions;

[INotifyPropertyChanged]
internal sealed partial class DecisionViewModel
{
    public DecisionViewModel(JobId job, string jobTitle, ITimelineItem card, DateTimeOffset since)
    {
        Job = job;
        JobTitle = jobTitle;
        Card = card;
        Since = since;
        Waiting = string.Empty;
    }

    public JobId Job { get; }

    public string JobTitle { get; }

    public ITimelineItem Card { get; }

    public DateTimeOffset Since { get; }

    [ObservableProperty]
    public partial string Waiting { get; private set; }

    public void Update(ITimelineEntry entry, DateTimeOffset now)
    {
        Card.Update(entry);
        Waiting = Waited(now - Since);
    }

    private static string Waited(TimeSpan waited) =>
        waited.TotalMinutes < 1 ? "waiting less than a minute"
        : waited.TotalHours < 1 ? string.Create(CultureInfo.InvariantCulture, $"waiting {(int)waited.TotalMinutes}m")
        : string.Create(CultureInfo.InvariantCulture, $"waiting {(int)waited.TotalHours}h {waited.Minutes}m");
}
