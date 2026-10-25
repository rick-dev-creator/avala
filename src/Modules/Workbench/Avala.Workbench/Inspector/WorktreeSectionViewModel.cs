using Avala.Components.Privacy;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Inspection;
using Avala.Workbench.Submitting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Inspector;

internal interface IWorktreeSectionViewModel
{
    bool IsLoaded { get; }

    string Branch { get; }

    string Fact { get; }

    string Base { get; }

    Personal Path { get; }

    string Ports { get; }

    bool IsScratch { get; }

    string Repository { get; }

    string FolderProblem { get; }

    bool CouldNotStart { get; }

    string StartProblem { get; }

    string RetryRefusal { get; }

    bool IsBranchCopied { get; }

    bool IsPathCopied { get; }

    IAsyncRelayCommand CopyBranchCommand { get; }

    IAsyncRelayCommand CopyPathCommand { get; }

    IAsyncRelayCommand OpenFolderCommand { get; }

    IAsyncRelayCommand RetryCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class WorktreeSectionViewModel : IWorktreeSectionViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private readonly InspectedJob inspected;
    private readonly ScratchFolders scratch;
    private readonly IJobLaunches launches;
    private readonly IClipboard clipboard;
    private Option<JobId> shown;

    public WorktreeSectionViewModel(InspectedJob inspected, ScratchFolders scratch, IJobLaunches launches, IClipboard clipboard)
    {
        this.inspected = inspected;
        this.scratch = scratch;
        this.launches = launches;
        this.clipboard = clipboard;
        inspected.Showing(Show, InspectorDepth.Complete);
    }

    public event EventHandler<Presented>? Presented
    {
        add => inspected.Presented += value;
        remove => inspected.Presented -= value;
    }

    public long Revision => inspected.Revision;

    public Task Loading => inspected.Loading;

    [ObservableProperty]
    public partial bool IsLoaded { get; private set; }

    [ObservableProperty]
    public partial string Fact { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyBranchCommand))]
    public partial string Branch { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyBranchCommand))]
    public partial string Base { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyPathCommand))]
    public partial Personal Path { get; private set; } = Personal.None;

    [ObservableProperty]
    public partial bool IsBranchCopied { get; private set; }

    [ObservableProperty]
    public partial bool IsPathCopied { get; private set; }

    [ObservableProperty]
    public partial string Ports { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    public partial bool IsScratch { get; private set; }

    [ObservableProperty]
    public partial string Repository { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string FolderProblem { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    public partial bool CouldNotStart { get; private set; }

    [ObservableProperty]
    public partial string StartProblem { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string RetryRefusal { get; private set; } = string.Empty;

    public void OnRegionContextChanged(Option<JobId> context) => inspected.Focus(context);

    public void Activate() => inspected.Activate();

    public void Deactivate() => inspected.Deactivate();

    public void Dispose() => inspected.Dispose();

    private static string Short(string commit) => commit[..Math.Min(7, commit.Length)];

    private bool HasBranch => Base.Length > 0;

    private bool HasPath => Path.Value.Length > 0;

    [RelayCommand(CanExecute = nameof(HasBranch))]
    private async Task CopyBranchAsync(CancellationToken cancellationToken)
    {
        IsBranchCopied = false;
        IsBranchCopied = await clipboard.CopyAsync(Branch, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(HasPath))]
    private async Task CopyPathAsync(CancellationToken cancellationToken)
    {
        IsPathCopied = false;
        IsPathCopied = await clipboard.CopyAsync(Path.Value, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(IsScratch))]
    private async Task OpenFolderAsync(CancellationToken cancellationToken) =>
        FolderProblem = await scratch.OpenAsync(Repository, cancellationToken) ? string.Empty : InspectorPhrases.FolderUnopened(Repository);

    [RelayCommand(CanExecute = nameof(CouldNotStart))]
    private async Task RetryAsync(CancellationToken cancellationToken) =>
        RetryRefusal = await shown.Match(
            async job => (await launches.RetryAsync(job, cancellationToken)).Match(_ => string.Empty, InspectorPhrases.NotRetried),
            () => Task.FromResult(string.Empty));

    private void Show(Option<InspectorFacts> facts)
    {
        IsLoaded = facts.IsSome;
        shown = facts.Map(found => found.Record.History.Summary.Job);
        ShowStart(facts);
        Repository = facts.Match(found => found.Record.History.Summary.Repository, () => string.Empty);
        IsScratch = scratch.Holds(Repository);
        FolderProblem = string.Empty;
        IsBranchCopied = false;
        IsPathCopied = false;
        ShowWorktree(facts);
        Ports = facts.Bind(found => found.Record.Ports).Match(
            lease => lease.First == lease.Last ? $"Port {lease.First}" : $"Ports {lease.First}–{lease.Last}",
            () => string.Empty);
    }

    private void ShowStart(Option<InspectorFacts> facts)
    {
        var summary = facts.Map(found => found.Record.History.Summary);
        CouldNotStart = summary.Match(job => job.Status == JobStatus.NeedsHelp && job.Hold == Option<HoldReason>.Some(HoldReason.CouldNotStart), () => false);
        StartProblem = CouldNotStart ? summary.Bind(job => job.Problem).Match(problem => problem, () => string.Empty) : string.Empty;
        RetryRefusal = string.Empty;
    }

    private void ShowWorktree(Option<InspectorFacts> facts)
    {
        var workspace = facts.Bind(found => found.Record.Workspace);
        var fact = facts.IsNone ? string.Empty : workspace.Match(found => InspectorPhrases.Worktree(found.Branch, found.BaseBranch), () => "none");
        Fact = CouldNotStart ? InspectorPhrases.CouldNotStart : IsScratch ? InspectorPhrases.Scratch(fact) : fact;
        Branch = facts.IsNone ? string.Empty : workspace.Match(found => found.Branch, () => "No worktree");
        ShowWhere(facts);
    }

    private void ShowWhere(Option<InspectorFacts> facts)
    {
        var workspace = facts.Bind(found => found.Record.Workspace);
        Base = workspace.Match(
            found => found.BaseBranch.Match(branch => $"{branch} at {Short(found.BaseCommit)}", () => Short(found.BaseCommit)),
            () => string.Empty);
        Path = workspace.Match(found => Personal.Path(found.Path), () => Personal.None);
    }
}
