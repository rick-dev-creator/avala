using System.Globalization;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Following;
using Avala.Workbench.Readying;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Readiness;

internal interface IAgentPullRequestNoteViewModel
{
    bool IsShown { get; }

    string Text { get; }

    IRelayCommand OpenSettingsCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class AgentPullRequestNoteViewModel(AgentPullRequests pullRequests, LiveFeed feed, IReadinessLinks links)
    : IAgentPullRequestNoteViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
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
    public partial string Text { get; private set; } = string.Empty;

    public void Activate() => feed.Start(token => pullRequests.OfAsync(job, token), Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    public void OnRegionContextChanged(Option<JobId> context)
    {
        job = context;
        feed.Refresh();
    }

    public static string Phrase(MentionedPullRequest pullRequest) =>
        string.Create(CultureInfo.InvariantCulture, $"Pull request #{pullRequest.Number} was opened by the agent. Connect {pullRequest.Link.Host} in Settings to follow its checks.");

    [RelayCommand]
    private void OpenSettings() => links.OpenSettings();

    private void Show(Option<MentionedPullRequest> mentioned)
    {
        IsShown = mentioned.IsSome;
        Text = mentioned.Match(Phrase, () => string.Empty);
    }
}
