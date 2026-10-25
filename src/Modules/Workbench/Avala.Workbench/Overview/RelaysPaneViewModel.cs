using Avala.Components.Graphs;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Overview;

internal sealed record RelaysPaneRequested(JobId Job);

internal sealed record RelaysPaneChanged(JobId Job, bool IsOpen);

internal sealed record RelaysPaneState(Option<JobId> Viewing, Option<RelaysView> View);

[INotifyPropertyChanged]
internal sealed partial class RelaysPaneViewModel
    : IRelaysPaneViewModel, IPane, IRegionAware<JobId>, IActivatable, IPresentation, IRecipient<RelaysPaneRequested>, IDisposable
{
    private readonly RelaysReader reader;
    private readonly LiveFeed feed;
    private readonly IMessenger messenger;
    private Option<JobId> job;

    public RelaysPaneViewModel(RelaysReader reader, LiveFeed feed, IMessenger messenger)
    {
        this.reader = reader;
        this.feed = feed;
        this.messenger = messenger;
        ReadProblem = new ReadProblemViewModel(feed);
        messenger.RegisterAll(this);
    }

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public Task Following => feed.Following;

    public IReadProblemViewModel ReadProblem { get; }

    [ObservableProperty]
    public partial bool HasJourney { get; private set; }

    [ObservableProperty]
    public partial string Caption { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Headline { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Subline { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsWaiting { get; private set; }

    [ObservableProperty]
    public partial RelayCourse Course { get; private set; } = RelayCourse.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<IRelayLaneViewModel> Lanes { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<IRelayLegViewModel> Legs { get; private set; } = [];

    [ObservableProperty]
    public partial string Began { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Latest { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Footer { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    IRelayCommand IRelaysPaneViewModel.CloseCommand => CloseNowCommand;

    public void Activate() => feed.Start(ReadAsync, Show);

    public void Deactivate() => feed.Stop();

    public void Dispose()
    {
        feed.Dispose();
        messenger.UnregisterAll(this);
    }

    public void OnRegionContextChanged(Option<JobId> context)
    {
        job = context;
        Toggle(IsOpen && job.Match(reader.Offers, () => false));
        feed.Refresh();
    }

    public void Receive(RelaysPaneRequested message)
    {
        if (job == Option<JobId>.Some(message.Job))
        {
            Toggle(!IsOpen);
            feed.Refresh();
        }
    }

    public void Close() => Toggle(false);

    [RelayCommand]
    private void CloseNow() => Toggle(false);

    private void Toggle(bool open)
    {
        if (IsOpen == open)
        {
            return;
        }

        IsOpen = open;

        foreach (var current in job.Match<JobId[]>(found => [found], () => []))
        {
            messenger.Send(new RelaysPaneChanged(current, open));
        }
    }

    private ValueTask<RelaysPaneState> ReadAsync(CancellationToken cancellationToken)
    {
        var shown = job;

        return ValueTask.FromResult(new RelaysPaneState(shown, IsOpen ? shown.Bind(reader.Read) : Option<RelaysView>.None));
    }

    private void Show(RelaysPaneState state)
    {
        if (state.Viewing != job)
        {
            return;
        }

        var view = state.View.Bind(found => found.Journey.IsEmpty ? Option<RelaysView>.None : found);
        HasJourney = view.IsSome;
        view.Match(
            found =>
            {
                Present(found);

                return true;
            },
            () =>
            {
                Course = RelayCourse.Empty;
                Lanes = [];
                Legs = [];

                return false;
            });
    }

    private void Present(RelaysView view)
    {
        var journey = view.Journey;
        var current = journey.Legs[^1];
        var threshold = journey.Stops.Select(mark => mark.Why).Concat(journey.Legs.Select(leg => leg.Handoff.Bind(handoff => handoff.Why)))
            .Select(why => why.Map(reason => reason.Threshold)).FirstOrDefault(found => found.IsSome);
        Caption = RelayPhrases.Caption(journey);
        Headline = RelayPhrases.Headline(current, view.Status);
        Subline = RelayPhrases.Subline(journey, current);
        IsWaiting = current.Kind == RelayLegKind.Waiting;
        Course = journey.Course();
        Lanes = [.. view.Gauges.Select(gauge => (IRelayLaneViewModel)new RelayLaneViewModel(gauge, threshold, gauge.Connection == current.Connection))];
        Legs = [.. journey.Legs.Select((leg, index) => (IRelayLegViewModel)new RelayLegViewModel(leg, Used(view.Gauges, leg), index == journey.Legs.Count - 1))];
        Began = RelayPhrases.Clock(journey.Legs[0].From);
        Latest = current.Kind == RelayLegKind.Ran ? RelayPhrases.Clock(current.To) : "now";
        Footer = RelayPhrases.Footer(view.Gauges, view.Now);
    }

    private static Option<double> Used(IReadOnlyList<RelayGauge> gauges, RelayLeg leg) =>
        gauges.Where(gauge => gauge.Connection == leg.Connection).Select(gauge => gauge.Tightest.Map(limit => limit.UsedFraction)).FirstOrDefault(Option<double>.None);
}
