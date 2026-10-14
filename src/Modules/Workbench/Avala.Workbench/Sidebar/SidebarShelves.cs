using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Projects;
using Avala.Workbench.Submitting;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Sidebar;

internal sealed class SidebarShelves
{
    private const string RepositoryKey = "repository:";

    private readonly ProjectShelf shelf;
    private readonly ScratchFolders scratch;
    private readonly IRelayCommand<object> select;
    private readonly ObservableCollection<ISidebarEntry> needsYou = [];
    private readonly ObservableCollection<IProjectGroupViewModel> projects = [];
    private readonly Dictionary<string, ProjectGroupViewModel> groups = [];
    private readonly Dictionary<ISidebarEntry, (ObservableCollection<ISidebarEntry> List, string Key)> places = new(ReferenceEqualityComparer.Instance);
    private ImmutableHashSet<string> collapsed = [];
    private Option<ProjectId> renaming;

    public SidebarShelves(ProjectShelf shelf, ScratchFolders scratch, IRelayCommand<object> select)
    {
        this.shelf = shelf;
        this.scratch = scratch;
        this.select = select;
        Chats = new ProjectGroupViewModel(shelf, select, new GroupHeading(ProjectGroupViewModel.ChatsKey, "Chats", Option<ProjectRecord>.None, true));
    }

    public IReadOnlyList<ISidebarEntry> NeedsYou => needsYou;

    public IReadOnlyList<IProjectGroupViewModel> Projects => projects;

    public ProjectGroupViewModel Chats { get; }

    public ProjectMap Map { get; private set; } = ProjectMap.Empty;

    public bool IsEmpty => needsYou.Count == 0 && !Chats.HasRows && projects.All(group => !group.HasRows);

    public static string KeyOf(ProjectId project) => project.Value.ToString();

    public string KeyOf(JobId job, string repository) =>
        Map.Of(job, repository).Match(
            project => KeyOf(project.Project),
            () => scratch.Holds(repository) ? ProjectGroupViewModel.ChatsKey : RepositoryKey + repository);

    public void Show(ProjectMap map)
    {
        Map = map;

        foreach (var project in map.Projects)
        {
            if (groups.TryGetValue(KeyOf(project.Project), out var known))
            {
                known.Show(project);
            }
            else
            {
                Add(new ProjectGroupViewModel(shelf, select, new GroupHeading(KeyOf(project.Project), project.Name, project, !collapsed.Contains(KeyOf(project.Project)))));
            }
        }

        Sort();
        RenamePending();
    }

    public void Place(ISidebarEntry entry, string key)
    {
        var target = entry.Group == JobGroup.NeedsYou ? needsYou : GroupFor(key).ListOf(entry.Group == JobGroup.Done);

        if (places.TryGetValue(entry, out var current))
        {
            places[entry] = (target, key);

            if (ReferenceEquals(current.List, target))
            {
                return;
            }

            current.List.Remove(entry);
        }

        target.Insert(target.TakeWhile(other => other.Submitted >= entry.Submitted).Count(), entry);
        places[entry] = (target, key);
    }

    public void Remove(ISidebarEntry entry)
    {
        if (places.Remove(entry, out var current))
        {
            current.List.Remove(entry);
        }
    }

    public Option<ISidebarEntry> NeighbourOf(ISidebarEntry entry)
    {
        if (!places.TryGetValue(entry, out var place))
        {
            return Option<ISidebarEntry>.None;
        }

        var others = place.List.Where(other => !ReferenceEquals(other, entry)).ToList();

        return others.Count == 0 ? Option<ISidebarEntry>.None : Option<ISidebarEntry>.Some(others[Math.Min(place.List.IndexOf(entry), others.Count - 1)]);
    }

    public void Prune()
    {
        foreach (var gone in groups.Values.Where(group => group.Project.Match(project => Map.Find(project.Project).IsNone, () => !group.HasRows)).ToList())
        {
            groups.Remove(gone.Key);
            projects.Remove(gone);
        }
    }

    public void Attend()
    {
        var waiting = needsYou.Select(entry => places.TryGetValue(entry, out var place) ? place.Key : string.Empty).ToHashSet();

        foreach (var group in groups.Values.Append(Chats))
        {
            group.NeedsYou = waiting.Contains(group.Key);
        }
    }

    public void Collapse(ImmutableHashSet<string> keys)
    {
        collapsed = keys;

        foreach (var group in groups.Values.Append(Chats))
        {
            group.Expand(!keys.Contains(group.Key));
        }
    }

    public void RenameWhenShown(ProjectId project)
    {
        renaming = project;
        RenamePending();
    }

    private ProjectGroupViewModel GroupFor(string key)
    {
        if (key == ProjectGroupViewModel.ChatsKey)
        {
            return Chats;
        }

        if (!groups.TryGetValue(key, out var group))
        {
            var repository = Path.TrimEndingDirectorySeparator(key[RepositoryKey.Length..]);
            group = new ProjectGroupViewModel(shelf, select, new GroupHeading(key, Path.GetFileName(repository) is { Length: > 0 } folder ? folder : repository, Option<ProjectRecord>.None, !collapsed.Contains(key)));
            Add(group);
            Sort();
        }

        return group;
    }

    private void Add(ProjectGroupViewModel group)
    {
        groups[group.Key] = group;
        projects.Add(group);
    }

    private void Sort()
    {
        var ordered = projects.OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase).ThenBy(group => group.Key, StringComparer.Ordinal).ToList();

        for (var index = 0; index < ordered.Count; index++)
        {
            var from = projects.IndexOf(ordered[index]);

            if (from != index)
            {
                projects.Move(from, index);
            }
        }
    }

    private void RenamePending()
    {
        if (renaming.Match(project => groups.GetValueOrDefault(KeyOf(project)), () => null) is { } group)
        {
            renaming = Option<ProjectId>.None;
            group.StartRename();
        }
    }
}
