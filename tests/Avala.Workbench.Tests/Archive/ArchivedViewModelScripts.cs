using Avala.Jobs.Contracts;
using Avala.Workbench.Archive;
using Avala.Workbench.Contracts.Presentation;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Archive;

public sealed class ArchivedViewModelScripts : IDisposable
{
    private readonly Archives archives = new();

    [Fact]
    public async Task ThePageListsArchivedConversationsNewestFirstByProjectWithChatsLastAsync()
    {
        var older = archives.Job("Paginate the export", JobStatus.Approved, Archives.Ledger, TimeSpan.FromDays(3));
        var newer = archives.Job("Update the dependency", JobStatus.AwaitingReview, Archives.Ledger, TimeSpan.FromHours(2));
        var chat = archives.Job("Sketch a CSV parser", JobStatus.AwaitingReview, Archives.Chat, TimeSpan.FromMinutes(20));
        var site = archives.Job("Fix the header", JobStatus.Discarded, Archives.Website, TimeSpan.FromDays(1));
        archives.Job("Add an endpoint", JobStatus.AwaitingReview, Archives.Ledger, TimeSpan.Zero);
        archives.Archive(older, newer, chat, site);
        using var page = archives.Page();
        page.Activate();

        await archives.Ui.PresentedAsync(page, () => page.HasArchived, () => page.Summary);

        Assert.Equal(["ledger", "website", "Chats"], page.Groups.Select(group => group.Name));
        Assert.Equal(["Update the dependency", "Paginate the export"], page.Groups[0].Rows.Select(row => row.Title));
        Assert.Equal("4 conversations, kept with their transcripts and records", page.Summary);
    }

    [Fact]
    public async Task WithNothingArchivedThePageSaysWhereArchivedConversationsShowAsync()
    {
        archives.Job("Add an endpoint", JobStatus.AwaitingReview, Archives.Ledger, TimeSpan.Zero);
        using var page = archives.Page();
        page.Activate();

        await archives.Ui.PresentedAsync(page, () => page.Revision > 0, () => page.Summary);

        Assert.Equal((false, 0, "Archived conversations show here"), (page.HasArchived, page.Groups.Count, page.Empty));
    }

    [Fact]
    public async Task RestoringARowRestoresItsConversationAsync()
    {
        var merged = archives.Job("Paginate the export", JobStatus.Approved, Archives.Ledger, TimeSpan.FromDays(3));
        archives.Archive(merged);
        using var page = archives.Page();
        page.Activate();
        await archives.Ui.PresentedAsync(page, () => page.HasArchived, () => page.Summary);

        await page.Groups[0].Rows[0].RestoreCommand.ExecuteAsync(null);

        Assert.Equal(["restore"], archives.Bench.ProjectBook.Calls);
        Assert.Empty(archives.Bench.ProjectBook.Current.Archived);
    }

    [Fact]
    public async Task OpeningARowShowsItsConversationAsync()
    {
        var merged = archives.Job("Paginate the export", JobStatus.Approved, Archives.Ledger, TimeSpan.FromDays(3));
        archives.Archive(merged);
        var selected = new List<JobId>();
        archives.Bench.Messenger.Register<List<JobId>, JobSelected>(selected, (list, message) => list.Add(message.Job));
        using var page = archives.Page();
        page.Activate();
        await archives.Ui.PresentedAsync(page, () => page.HasArchived, () => page.Summary);

        page.Groups[0].Rows[0].OpenCommand.Execute(null);

        Assert.Equal([merged.Job], selected);
    }

    public void Dispose() => archives.Dispose();
}
