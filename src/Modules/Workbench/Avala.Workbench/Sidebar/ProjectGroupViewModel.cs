using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avala.Projects.Contracts;
using Avala.Sdk;
using Avala.Workbench.Projects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Sidebar;

internal sealed record GroupHeading(string Key, string Name, Option<ProjectRecord> Project, bool Expanded);

internal interface ISidebarCommands
{
    IRelayCommand<object> SelectCommand { get; }
}

internal interface IProjectGroupViewModel : ISidebarCommands
{
    string Key { get; }

    string Name { get; }

    bool IsChats { get; }

    bool IsProject { get; }

    int OpenCount { get; }

    bool HasRows { get; }

    bool NeedsYou { get; }

    bool IsExpanded { get; }

    IReadOnlyList<ISidebarEntry> Open { get; }

    IReadOnlyList<ISidebarEntry> Done { get; }

    bool HasDone { get; }

    string DoneLabel { get; }

    bool IsDoneShown { get; }

    bool IsRenaming { get; }

    int Renames { get; }

    string Draft { get; set; }

    string Problem { get; }

    bool HasProblem { get; }

    bool CanDelete { get; }

    IAsyncRelayCommand ToggleCommand { get; }

    IRelayCommand ToggleDoneCommand { get; }

    IRelayCommand StartRenameCommand { get; }

    IAsyncRelayCommand CommitRenameCommand { get; }

    IRelayCommand CancelRenameCommand { get; }

    IAsyncRelayCommand DeleteCommand { get; }

    IAsyncRelayCommand ArchiveDoneCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ProjectGroupViewModel : IProjectGroupViewModel
{
    public const string ChatsKey = "chats";

    private readonly ProjectShelf shelf;
    private readonly ObservableCollection<ISidebarEntry> open = [];
    private readonly ObservableCollection<ISidebarEntry> done = [];

    public ProjectGroupViewModel(ProjectShelf shelf, IRelayCommand<object> select, GroupHeading heading)
    {
        this.shelf = shelf;
        SelectCommand = select;
        Key = heading.Key;
        Name = heading.Name;
        Project = heading.Project;
        IsExpanded = heading.Expanded;
        Draft = heading.Name;
        Problem = string.Empty;
        open.CollectionChanged += Counted;
        done.CollectionChanged += Counted;
    }

    public string Key { get; }

    public Option<ProjectRecord> Project { get; private set; }

    public IRelayCommand<object> SelectCommand { get; }

    public bool IsChats => Key == ChatsKey;

    public bool IsProject => Project.IsSome;

    public IReadOnlyList<ISidebarEntry> Open => open;

    public IReadOnlyList<ISidebarEntry> Done => done;

    public int OpenCount => open.Count;

    public bool HasRows => open.Count + done.Count > 0;

    public bool HasDone => done.Count > 0;

    public string DoneLabel => ProjectPhrases.Done(done.Count);

    public bool CanDelete => Project.Match(project => project.Repository.IsNone || !HasRows, () => false);

    public bool HasProblem => Problem.Length > 0;

    [ObservableProperty]
    public partial string Name { get; private set; }

    [ObservableProperty]
    public partial bool NeedsYou { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; private set; }

    [ObservableProperty]
    public partial bool IsDoneShown { get; private set; }

    [ObservableProperty]
    public partial bool IsRenaming { get; private set; }

    [ObservableProperty]
    public partial int Renames { get; private set; }

    [ObservableProperty]
    public partial string Draft { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string Problem { get; private set; }

    public ObservableCollection<ISidebarEntry> ListOf(bool ended) => ended ? done : open;

    public void Show(ProjectRecord project)
    {
        Project = project;
        Name = project.Name;
        OnPropertyChanged(nameof(IsProject));
        OnPropertyChanged(nameof(CanDelete));
        StartRenameCommand.NotifyCanExecuteChanged();
    }

    public void Expand(bool expanded) => IsExpanded = expanded;

    [RelayCommand]
    private Task ToggleAsync(CancellationToken cancellationToken)
    {
        IsExpanded = !IsExpanded;

        return shelf.RememberAsync(Key, !IsExpanded, cancellationToken).AsTask();
    }

    [RelayCommand]
    private void ToggleDone() => IsDoneShown = !IsDoneShown;

    [RelayCommand(CanExecute = nameof(IsProject))]
    public void StartRename()
    {
        Draft = Name;
        Problem = string.Empty;
        IsRenaming = true;
        Renames++;
    }

    [RelayCommand]
    private async Task CommitRenameAsync(CancellationToken cancellationToken)
    {
        if (!IsRenaming || Project.Match(found => found.Project, () => default(ProjectId?)) is not { } project)
        {
            return;
        }

        var refused = await shelf.RenameAsync(project, Draft, cancellationToken);
        Problem = refused.Match(ProjectPhrases.Refusal, () => string.Empty);
        IsRenaming = refused.IsSome;
    }

    [RelayCommand]
    private void CancelRename()
    {
        IsRenaming = false;
        Problem = string.Empty;
        Draft = Name;
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (Project.Match(found => found.Project, () => default(ProjectId?)) is { } project)
        {
            Problem = (await shelf.DeleteAsync(project, cancellationToken)).Match(ProjectPhrases.Refusal, () => string.Empty);
        }
    }

    [RelayCommand(CanExecute = nameof(HasDone))]
    private async Task ArchiveDoneAsync(CancellationToken cancellationToken) =>
        Problem = (await shelf.ArchiveAsync([.. done.SelectMany(entry => entry.Shelved)], cancellationToken)).Match(ProjectPhrases.Refusal, () => string.Empty);

    private void Counted(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ArchiveDoneCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(OpenCount));
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasDone));
        OnPropertyChanged(nameof(DoneLabel));
        OnPropertyChanged(nameof(CanDelete));
    }
}
