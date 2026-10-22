using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Workbench.Notes;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Notes;

public sealed class NoteBoardViewModelScripts : IDisposable
{
    private readonly FakeNotes notes = new();
    private readonly FakeIdeas ideas = new();
    private readonly NotePlace place = NotePlace.OfJob(JobId.New(), "/repo");
    private readonly NoteBoardViewModel board;

    public NoteBoardViewModelScripts() => board = new NoteBoardViewModel(notes, ideas, new FakePictures(), new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddDays(10)));

    public void Dispose() => board.Dispose();

    [Fact]
    public async Task SavingANewNoteTakesItAndListsItFirstAsync()
    {
        notes.Add(place, "Plan", "Split the form.", DateTimeOffset.UnixEpoch);
        await ShowAsync();

        board.NewCommand.Execute(null);
        board.Title = "Login bug";
        board.Body = "The button overlaps.";
        await board.SaveCommand.ExecuteAsync(null);

        var taken = (await notes.ListAsync(place, TestContext.Current.CancellationToken))[0];
        Assert.Equal(("Login bug", "The button overlaps."), (taken.Title, taken.Body));
        Assert.Equal(["Login bug", "Plan"], board.Notes.Select(note => note.Title));
        Assert.False(board.IsEditing);
    }

    [Fact]
    public async Task SavingAnOpenedNoteEditsItAsync()
    {
        var kept = notes.Add(place, "Login bug", "The button overlaps.", DateTimeOffset.UnixEpoch);
        await ShowAsync();

        board.Notes[0].OpenCommand.Execute(null);
        var opened = (board.Title, board.Body, board.Notes[0].IsSelected);
        board.Title = "Login button bug";
        await board.SaveCommand.ExecuteAsync(null);

        Assert.Equal(("Login bug", "The button overlaps.", true), opened);
        Assert.Equal(("Login button bug", "The button overlaps."), (notes.Find(kept.Id).Title, notes.Find(kept.Id).Body));
        Assert.Equal(["Login button bug"], board.Notes.Select(note => note.Title));
    }

    [Fact]
    public async Task ArchivingAnOpenedNoteRemovesItFromTheListAsync()
    {
        var kept = notes.Add(place, "Login bug", "The button overlaps.", DateTimeOffset.UnixEpoch);
        await ShowAsync();

        board.Notes[0].OpenCommand.Execute(null);
        await board.ArchiveCommand.ExecuteAsync(null);

        Assert.Equal(NoteState.Archived, notes.Find(kept.Id).State);
        Assert.Equal((false, 0), (board.IsEditing, board.Notes.Count));
    }

    [Fact]
    public async Task MakingAnOpenedNoteAnIdeaMarksItsRowAndKeepsItInTheListAsync()
    {
        var kept = notes.Add(place, "Login bug", "The button overlaps.", DateTimeOffset.UnixEpoch);
        await ShowAsync();

        board.Notes[0].OpenCommand.Execute(null);
        var offered = board.CanMakeIdea;
        await board.MakeIdeaCommand.ExecuteAsync(null);

        Assert.Equal((true, NoteState.Idea), (offered, notes.Find(kept.Id).State));
        Assert.Equal((false, 1, true), (board.IsEditing, board.Notes.Count, board.Notes[0].IsIdea));
    }

    [Fact]
    public async Task StartingAJobFromAnOpenedIdeaHandsTheIdeaOverAndSaysSoAsync()
    {
        var idea = notes.Add(place, "Login bug", "The button overlaps.", DateTimeOffset.UnixEpoch, NoteState.Idea);
        await ShowAsync();

        board.Notes[0].OpenCommand.Execute(null);
        var offered = board.CanStartJob;
        await board.StartJobCommand.ExecuteAsync(null);

        Assert.Equal((true, true), (offered, board.HasNotice));
        Assert.Equal([idea.Id], ideas.Started);
        Assert.Equal(NotePhrases.JobStarted("Login bug"), board.Notice);
    }

    [Fact]
    public async Task PublishingAnOpenedIdeaAsAnIssueHandsTheIdeaOverAndShowsWhereItLivesAsync()
    {
        var idea = notes.Add(place, "Login bug", "The button overlaps.", DateTimeOffset.UnixEpoch, NoteState.Idea);
        await ShowAsync();

        board.Notes[0].OpenCommand.Execute(null);
        await board.PublishIssueCommand.ExecuteAsync(null);

        Assert.Equal([idea.Id], ideas.Published);
        Assert.Equal(NotePhrases.IssuePublished(new Uri("https://forge.example.com/acme/shop/issues/7")), board.Notice);
    }

    [Fact]
    public async Task ARefusedIssueSaysWhyAsync()
    {
        notes.Add(place, "Login bug", "The button overlaps.", DateTimeOffset.UnixEpoch, NoteState.Idea);
        ideas.Refusal = IdeaRejection.IssueRefused;
        await ShowAsync();

        board.Notes[0].OpenCommand.Execute(null);
        await board.PublishIssueCommand.ExecuteAsync(null);

        Assert.Equal((false, NotePhrases.Rejection(IdeaRejection.IssueRefused)), (board.HasNotice, board.Problem));
    }

    [Fact]
    public async Task ARefusedJobStartSaysWhyAsync()
    {
        notes.Add(place, "Login bug", "The button overlaps.", DateTimeOffset.UnixEpoch, NoteState.Idea);
        ideas.Refusal = IdeaRejection.JobRefused;
        await ShowAsync();

        board.Notes[0].OpenCommand.Execute(null);
        await board.StartJobCommand.ExecuteAsync(null);

        Assert.Equal((false, NotePhrases.Rejection(IdeaRejection.JobRefused)), (board.HasNotice, board.Problem));
    }

    [Fact]
    public async Task ANoteThatIsNotAnIdeaOffersNoJobAsync()
    {
        notes.Add(place, "Login bug", "The button overlaps.", DateTimeOffset.UnixEpoch);
        await ShowAsync();

        board.Notes[0].OpenCommand.Execute(null);

        Assert.False(board.CanStartJob);
    }

    [Fact]
    public async Task AnIdeaCannotBeMadeAnIdeaAgainAsync()
    {
        notes.Add(place, "Login bug", "The button overlaps.", DateTimeOffset.UnixEpoch, NoteState.Idea);
        await ShowAsync();

        board.Notes[0].OpenCommand.Execute(null);

        Assert.False(board.CanMakeIdea);
    }

    [Fact]
    public async Task ARefusedSaveSaysWhyAndKeepsTheDraftOpenAsync()
    {
        await ShowAsync();
        notes.Refusal = NoteRejection.UntitledNote;

        board.NewCommand.Execute(null);
        await board.SaveCommand.ExecuteAsync(null);

        Assert.Equal((true, "Give the note a title before saving it."), (board.IsEditing, board.Problem));
    }

    [Fact]
    public async Task OpeningANoteShowsItsImagesAsThumbnailsAsync()
    {
        var kept = notes.Add(place, "Login bug", "The button overlaps.", DateTimeOffset.UnixEpoch) with
        {
            Media = [new NoteMedia("login.png", "image/png"), new NoteMedia("trace.txt", "text/plain")],
            Root = "/repo",
            Folder = ".avala/notes/login-bug",
        };
        board.Show(place, [kept]);

        board.Notes[0].OpenCommand.Execute(null);

        Assert.Equal(["login.png"], board.Images.Select(image => image.Name));
        Assert.Equal("1 image", board.Notes[0].Images);
    }

    private async Task ShowAsync() => board.Show(place, await board.ListAsync(place, TestContext.Current.CancellationToken));
}
