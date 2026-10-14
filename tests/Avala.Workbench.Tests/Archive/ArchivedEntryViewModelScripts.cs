using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Archive;
using Avala.Sdk.Presentation;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Archive;

public sealed class ArchivedEntryViewModelScripts : IDisposable
{
    private readonly Archives archives = new();

    [Fact]
    public async Task TheEntryCountsArchivedConversationsAndHidesWithoutAnyAsync()
    {
        var merged = archives.Job("Paginate the export", JobStatus.Approved, Archives.Ledger, TimeSpan.FromDays(3));
        var dropped = archives.Job("Try a cache", JobStatus.Discarded, Archives.Ledger, TimeSpan.FromDays(2));
        using var entry = Entry();
        await archives.Ui.PresentedAsync(entry, () => entry.Revision > 0, () => entry.Count);
        var before = entry.HasArchived;

        archives.Archive(merged, dropped);
        await archives.Ui.PresentedAsync(entry, () => entry.HasArchived, () => entry.Count);

        Assert.Equal((false, true, "2"), (before, entry.HasArchived, entry.Count));
    }

    [Fact]
    public void OpeningTheEntryShowsTheArchivedPage()
    {
        var requested = new List<IPage>();
        archives.Bench.Messenger.Register<List<IPage>, PageRequested>(requested, (list, message) => list.Add(message.Page));
        var page = archives.Page();
        using var entry = new ArchivedEntryViewModel(archives.Reader, archives.Feed(), archives.Bench.Focus, page);

        entry.OpenCommand.Execute(null);

        Assert.Same(page, Assert.Single(requested));
    }

    public void Dispose() => archives.Dispose();

    private ArchivedEntryViewModel Entry()
    {
        var entry = new ArchivedEntryViewModel(archives.Reader, archives.Feed(), archives.Bench.Focus, archives.Page());
        entry.Activate();

        return entry;
    }
}
