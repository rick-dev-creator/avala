using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class MediaViewerViewModelScripts : IDisposable
{
    private readonly SessionId session = SessionId.New();
    private readonly TurnId turn = TurnId.New();
    private readonly TemporaryFolder folder = new();
    private readonly FakePictures pictures = new();

    public void Dispose() => folder.Dispose();

    [Fact]
    public async Task ClickingAnImageOpensItLargeWithItsCaptionAndItsPlaceAmongTheJobsImagesAsync()
    {
        var conversation = await ShownAsync("login.png", "checkout.png", "receipt.png");
        var chosen = conversation.Entries.OfType<MediaViewModel>().ElementAt(1);

        chosen.OpenCommand.Execute(null);
        await SettledAsync(conversation.Viewer);

        Assert.Equal((true, "checkout.png", "The checkout.png screen", "2 of 3", true), (conversation.Viewer.IsOpen, conversation.Viewer.Name, conversation.Viewer.Caption, conversation.Viewer.Position, conversation.Viewer.HasMany));
        Assert.Equal(MediaViewModel.PictureWidth, Assert.IsType<FakePicture>(conversation.Viewer.Picture).Width);
    }

    [Fact]
    public async Task NextAndPreviousStepThroughAllTheJobsImagesAndWrapAroundAsync()
    {
        var conversation = await ShownAsync("login.png", "checkout.png", "receipt.png");
        conversation.Entries.OfType<MediaViewModel>().Last().OpenCommand.Execute(null);
        var viewer = conversation.Viewer;
        var seen = new List<string>();

        await viewer.NextCommand.ExecuteAsync(null);
        seen.Add(viewer.Name);
        await viewer.NextCommand.ExecuteAsync(null);
        seen.Add(viewer.Name);
        await viewer.PreviousCommand.ExecuteAsync(null);
        seen.Add(viewer.Name);
        await viewer.PreviousCommand.ExecuteAsync(null);
        seen.Add(viewer.Name);

        Assert.Equal(["login.png", "checkout.png", "login.png", "receipt.png"], seen);
    }

    [Fact]
    public async Task ASingleImageOffersNoStepsAsync()
    {
        var conversation = await ShownAsync("login.png");

        conversation.Entries.OfType<MediaViewModel>().Single().OpenCommand.Execute(null);
        await SettledAsync(conversation.Viewer);

        Assert.Equal((false, false, false), (conversation.Viewer.HasMany, conversation.Viewer.NextCommand.CanExecute(null), conversation.Viewer.PreviousCommand.CanExecute(null)));
    }

    [Fact]
    public async Task ClosingTheViewerReleasesTheLargePictureAsync()
    {
        var conversation = await ShownAsync("login.png", "checkout.png");
        conversation.Entries.OfType<MediaViewModel>().First().OpenCommand.Execute(null);
        await SettledAsync(conversation.Viewer);
        await conversation.Viewer.NextCommand.ExecuteAsync(null);

        await conversation.Viewer.CloseCommand.ExecuteAsync(null);

        Assert.Equal((false, true), (conversation.Viewer.IsOpen, conversation.Viewer.Picture.IsNothing));
        Assert.Empty(pictures.Held);
    }

    [Fact]
    public async Task AWiderSheetDecodesTheImageAtItsWidthAsync()
    {
        var conversation = await ShownAsync("login.png");
        conversation.Entries.OfType<MediaViewModel>().Single().OpenCommand.Execute(null);
        await SettledAsync(conversation.Viewer);

        await conversation.Viewer.FitCommand.ExecuteAsync(1400);

        Assert.Equal([1400], pictures.Held);
    }

    private static Task SettledAsync(IMediaViewerViewModel viewer) => ((MediaViewerViewModel)viewer).StepCommand.ExecutionTask ?? Task.CompletedTask;

    private async Task<ConversationViewModel> ShownAsync(params string[] names)
    {
        var transcript = Transcript.Empty.WithPrompts("Show me the checkout", []).Apply(new TurnStarted(session, turn), DateTimeOffset.UnixEpoch);

        foreach (var name in names)
        {
            var path = Path.Combine(folder.Path, name);
            await File.WriteAllBytesAsync(path, [137, 80, 78, 71], TestContext.Current.CancellationToken);
            transcript = transcript.Apply(new MediaShown(session, turn, new ItemId(name), new Attachment(name, "image/png", path), $"The {name} screen"));
        }

        var jobs = new FakeJobs();
        var conversation = new Conversations(
            Composing.Of(new JobSteering(jobs, new QueuedMessages(jobs)), new SendKeys()),
            new TimelineItems(Replying.With(new FakePermissionAnswers()), new FakeClipboard(), Tools.Noting(), new FileViewing(Tools.Files(), FakeLinks.Opening, pictures, Tools.Replies(FakeLinks.Opening))),
            new TranscriptExport(new FakeClipboard(), new FakeFileSaver(), new FakeAppearance())).Open(JobId.New());
        conversation.Show(new BoardJob(new FakeCatalog().Add("Show me the checkout").Summary, transcript));

        return conversation;
    }
}
