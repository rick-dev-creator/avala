using System.Collections.ObjectModel;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Archiving;
using Avala.Workbench.Following;
using Avala.Workbench.Navigation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Projects;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Archive;

internal interface IArchivedViewModel
{
    string Title { get; }

    string Summary { get; }

    bool HasArchived { get; }

    string Empty { get; }

    IReadOnlyList<ArchivedGroup> Groups { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ArchivedViewModel(ArchivedReader reader, LiveFeed feed, ProjectShelf shelf, JobFocus focus)
    : IArchivedViewModel, IPage, IActivatable, IPresentation, IDisposable
{
    private readonly ObservableCollection<ArchivedGroup> groups = [];

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public Task Following => feed.Following;

    public string Title => "Archived";

    public PagePlacement Placement => PagePlacement.Hidden;

    public string Empty => ArchivePhrases.Empty;

    public IReadOnlyList<ArchivedGroup> Groups => groups;

    [ObservableProperty]
    public partial string Summary { get; private set; } = ArchivePhrases.Summary(0);

    [ObservableProperty]
    public partial bool HasArchived { get; private set; }

    public void Activate() => feed.Start(_ => ValueTask.FromResult((Shelves: reader.Read(), reader.Now)), Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    private void Show((IReadOnlyList<ArchivedShelf> Shelves, DateTimeOffset Now) state)
    {
        groups.Clear();

        foreach (var shelved in state.Shelves)
        {
            groups.Add(new ArchivedGroup(
                shelved.Project,
                shelved.Conversations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [.. shelved.Conversations.Select(conversation => new ArchivedRowViewModel(conversation, state.Now, focus.Select, RestoreAsync))]));
        }

        var count = state.Shelves.Sum(shelved => shelved.Conversations.Count);
        Summary = ArchivePhrases.Summary(count);
        HasArchived = count > 0;
    }

    private async Task RestoreAsync(IReadOnlyList<JobId> jobs, CancellationToken cancellationToken)
    {
        foreach (var job in jobs)
        {
            await shelf.RestoreAsync(job, cancellationToken);
        }
    }
}
