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

    string Path { get; }

    string Ports { get; }

    bool IsScratch { get; }

    string Repository { get; }

    string FolderProblem { get; }

    IAsyncRelayCommand OpenFolderCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class WorktreeSectionViewModel : IWorktreeSectionViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private readonly InspectedJob inspected;
    private readonly ScratchFolders scratch;

    public WorktreeSectionViewModel(InspectedJob inspected, ScratchFolders scratch)
    {
        this.inspected = inspected;
        this.scratch = scratch;
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
    public partial string Branch { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Base { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Path { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Ports { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    public partial bool IsScratch { get; private set; }

    [ObservableProperty]
    public partial string Repository { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string FolderProblem { get; private set; } = string.Empty;

    public void OnRegionContextChanged(Option<JobId> context) => inspected.Focus(context);

    public void Activate() => inspected.Activate();

    public void Deactivate() => inspected.Deactivate();

    public void Dispose() => inspected.Dispose();

    private static string Short(string commit) => commit[..Math.Min(7, commit.Length)];

    [RelayCommand(CanExecute = nameof(IsScratch))]
    private async Task OpenFolderAsync(CancellationToken cancellationToken) =>
        FolderProblem = await scratch.OpenAsync(Repository, cancellationToken) ? string.Empty : InspectorPhrases.FolderUnopened(Repository);

    private void Show(Option<InspectorFacts> facts)
    {
        IsLoaded = facts.IsSome;
        Repository = facts.Match(found => found.Record.History.Summary.Repository, () => string.Empty);
        IsScratch = scratch.Holds(Repository);
        FolderProblem = string.Empty;
        var workspace = facts.Bind(found => found.Record.Workspace);
        var fact = facts.IsNone ? string.Empty : workspace.Match(found => InspectorPhrases.Worktree(found.Branch, found.BaseBranch), () => "none");
        Fact = IsScratch ? InspectorPhrases.Scratch(fact) : fact;
        Branch = facts.IsNone ? string.Empty : workspace.Match(found => found.Branch, () => "No worktree");
        Base = workspace.Match(
            found => found.BaseBranch.Match(branch => $"{branch} at {Short(found.BaseCommit)}", () => Short(found.BaseCommit)),
            () => string.Empty);
        Path = workspace.Match(found => found.Path, () => string.Empty);
        Ports = facts.Bind(found => found.Record.Ports).Match(
            lease => lease.First == lease.Last ? $"Port {lease.First}" : $"Ports {lease.First}–{lease.Last}",
            () => string.Empty);
    }
}
