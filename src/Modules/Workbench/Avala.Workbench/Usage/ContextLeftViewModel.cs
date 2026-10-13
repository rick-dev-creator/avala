using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Components.Meters;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Usage;

internal interface IContextLeftViewModel
{
    bool IsShown { get; }

    string Share { get; }

    double Used { get; }

    bool IsNear { get; }

    bool WasCompacted { get; }

    string Tokens { get; }

    string Model { get; }

    string Compaction { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ContextLeftViewModel(JobBoard board, LiveFeed feed)
    : IContextLeftViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private Option<JobId> job;

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public Task Following => feed.Following;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial string Share { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial double Used { get; private set; }

    [ObservableProperty]
    public partial bool IsNear { get; private set; }

    [ObservableProperty]
    public partial bool WasCompacted { get; private set; }

    [ObservableProperty]
    public partial string Tokens { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Model { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Compaction { get; private set; } = string.Empty;

    public void Activate() => feed.Start(_ => ValueTask.FromResult(job.Bind(board.Find)), Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    public void OnRegionContextChanged(Option<JobId> context)
    {
        job = context;
        feed.Refresh();
    }

    private void Show(Option<BoardJob> shown)
    {
        var context = shown.Bind(found => found.Transcript.Context).Match(
            reported => reported.Window > 0 ? reported : Option<ContextReported>.None,
            () => Option<ContextReported>.None);
        var used = context.Match(reported => Math.Clamp((double)reported.Used / reported.Window, 0, 1), () => 0d);
        Used = used;
        Share = context.Match(_ => ContextPhrases.Share(used), () => string.Empty);
        IsNear = context.IsSome && used >= 1 - MeterViewModel.AttentionMargin;
        WasCompacted = context.Match(reported => reported.Compacted, () => false);
        Tokens = context.Match(ContextPhrases.Tokens, () => string.Empty);
        Model = shown.Bind(found => found.Transcript.Ran).Match(ran => ran.Model, () => string.Empty);
        Compaction = WasCompacted ? ContextPhrases.Compacted : string.Empty;
        IsShown = context.IsSome;
    }
}

internal static class ContextPhrases
{
    public const string Compacted = "Compacted during the last turn, so older messages were summarized.";

    public static string Share(double used) => $"{Amounts.Percent(1 - used)} context left";

    public static string Tokens(ContextReported context) =>
        string.Create(CultureInfo.InvariantCulture, $"{context.Used:N0} of {context.Window:N0} tokens in use");
}
