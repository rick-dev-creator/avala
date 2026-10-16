using System.Collections.Immutable;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Components.Pictures;
using Avala.Components.UI.Pictures;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Testing.UI;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Mentioning;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;
using Avala.Workspaces.Contracts;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Avala.Workbench.Tests.Views;

public sealed class ContextFeatureScreens(HeadlessUi ui)
{
    private const string Gate = "AVALA_FEATURE_SCREENS";
    private const string Instruction = "Build sistema-solar.html: the sun and the eight planets orbiting it, in one page";
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private readonly SessionId session = SessionId.New();
    private readonly TurnId turn = TurnId.New();

    private static string? Folder => Environment.GetEnvironmentVariable(Gate) is { Length: > 0 } found ? found : null;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Variants => new("dark", "light");

    [Theory]
    [MemberData(nameof(Variants))]
    public Task FindInTheConversationAsync(string variant) =>
        ShootAsync(async scratch =>
        {
            var conversation = Conversation(scratch, []);
            var view = Show(conversation, variant);
            var search = (ConversationSearchViewModel)conversation.Search;

            search.OpenCommand.Execute(null);
            search.Query = "orbit";
            search.NextCommand.Execute(null);
            await PumpAsync(view);

            Assert.True(Save(view, $"context-find-{variant}"));
        });

    [Theory]
    [MemberData(nameof(Variants))]
    public Task AttachmentsInTheComposerAndUnderThePromptAsync(string variant) =>
        ShootAsync(async scratch =>
        {
            var screenshot = Path.Combine(scratch, "orbits.png");
            await File.WriteAllBytesAsync(screenshot, ChatFeatureScreens.Orbits(), Cancellation);
            var sent = new Attachment("orbits.png", "image/png", screenshot);
            var conversation = Conversation(scratch, [sent, new Attachment("kepler-notes.pdf", "application/pdf", Path.Combine(scratch, "kepler-notes.pdf"))]);
            var view = Show(conversation, variant);
            var composer = (ComposerViewModel)conversation.Composer;
            var tray = (AttachmentTrayViewModel)composer.Attachments;
            tray.Accept(new AcceptsAttachments(["image/png", "application/pdf", "text/*"]));

            await tray.DropCommand.ExecuteAsync([screenshot, Path.Combine(scratch, "planets.csv")]);
            composer.Draft = "Match the colors of this screenshot and use the radii in the table";
            await PumpAsync(view);

            for (var round = 0; round < 3; round++)
            {
                foreach (var chip in view.All<UserControl>().Select(control => control.DataContext).OfType<AttachmentChipViewModel>())
                {
                    await (chip.ShowCommand.ExecutionTask ?? Task.CompletedTask);
                }

                await PumpAsync(view);
            }

            Assert.True(Save(view, $"context-attachments-{variant}"));
        });

    [Theory]
    [MemberData(nameof(Variants))]
    public Task MentionsAboveTheComposerAsync(string variant) =>
        ShootAsync(async scratch =>
        {
            var conversation = Conversation(scratch, []);
            var view = Show(conversation, variant);
            var composer = (ComposerViewModel)conversation.Composer;

            composer.Draft = "Keep the scale of @orb";
            await ((MentionsViewModel)composer.Mentions).Looking;
            ((MentionsViewModel)composer.Mentions).NextCommand.Execute(null);
            await PumpAsync(view);

            Assert.True(Save(view, $"context-mentions-{variant}"));
        });

    private ConversationViewModel Conversation(string scratch, IReadOnlyList<Attachment> attached)
    {
        var worktree = new Worktree();
        var worked = worktree.Worked();
        var index = new FakeRepositoryIndex();
        index.Files.AddRange(["sistema-solar.html", "src/orbits.js", "src/orbit-scale.js", "src/planets.js", "docs/orbits.md", "assets/orbits.png"]);
        index.Commits.AddRange([new CommitSummary("a3f9c21", "Scale the orbits by the real distances"), new CommitSummary("7be04d2", "Draw the sun and the planets")]);
        var board = new JobBoard();
        var other = new FakeCatalog().Add("Explain why the outer orbits look squeezed", JobStatus.AwaitingReview).Summary;
        board.Publish(ImmutableDictionary<JobId, BoardJob>.Empty.Add(other.Job, new BoardJob(other, Transcript.Empty)));
        var jobs = new FakeJobs { Status = JobStatus.NeedsHelp };
        var parts = new ComposerParts(new AttachmentTrays(new ScreenAttachments(scratch), new FakeFilePicker(), new PictureCache()), new MentionSource(worked, index, board, TimeProvider.System), Composing.Drafts());
        var composers = new Composers(new JobSteering(jobs, new QueuedMessages(jobs)), new SendKeys(), Composing.Harnesses(jobs, new FakeHandoffRequests(), new FakeConnections()), parts);
        var items = new TimelineItems(Replying.With(new FakePermissionAnswers()), new FakeClipboard(), new FileViewing(worked, FakeLinks.Opening, new PictureCache(), Tools.Replies(FakeLinks.Opening)));
        var conversation = new Conversations(composers, items, new TranscriptExport(new FakeClipboard(), new FakeFileSaver(), new FakeAppearance())).Open(worktree.Job);
        var summary = new FakeCatalog().Add(Instruction, JobStatus.NeedsHelp).Summary with { Job = worktree.Job };
        conversation.Show(new BoardJob(summary, Finished(attached)));

        return conversation;
    }

    private Transcript Finished(IReadOnlyList<Attachment> attached) =>
        Transcript.Empty.WithPrompts(Instruction, [new AttemptRecord(1, AttemptOrigin.Initial, AttemptOutcome.Passed, Option<string>.None, session) { Attachments = attached }])
            .Apply(new TurnStarted(session, turn), Start)
            .Apply(new ItemStarted(session, turn, new ItemId("page"), ItemKind.FileEdit, "Write sistema-solar.html") { Input = "<canvas id=\"orbits\"></canvas>" }, Start.AddSeconds(2))
            .Apply(new ItemCompleted(session, turn, new ItemId("page"), ItemOutcome.Succeeded), Start.AddSeconds(3))
            .Apply(new ItemStarted(session, turn, new ItemId("done"), ItemKind.Message, "Message"), Start.AddSeconds(4))
            .Apply(new ItemProgressed(session, turn, new ItemId("done"), "Done. **sistema-solar.html** draws the sun and the eight planets, each on its own orbit.\n\n- Orbits scale with the real distances, squeezed so Neptune still fits.\n- Periods follow Kepler's third law, so the inner orbits turn fastest."), Start.AddSeconds(5))
            .Apply(new ItemCompleted(session, turn, new ItemId("done"), ItemOutcome.Succeeded), Start.AddSeconds(6))
            .Apply(new TurnCompleted(session, turn, TurnOutcome.Finished), Start.AddSeconds(7));

    private Task ShootAsync(Func<string, Task> shoot)
    {
        Assert.SkipUnless(Folder is not null && HeadlessApp.Renders, $"Set {Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the context screens.");

        return ui.RunAsync(async () =>
        {
            using var scratch = new TemporaryFolder();
            await shoot(scratch.Path);
        }, Cancellation);
    }

    private static ViewScript Show(ConversationViewModel conversation, string variant)
    {
        var view = Screen.Show(conversation, variant == "light" ? ThemeVariant.Light : ThemeVariant.Dark, 1000, 700);
        view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
        Components.UI.Theme.Motion.SetIsReduced(view.Window, true);

        return view;
    }

    private static async Task PumpAsync(ViewScript view)
    {
        for (var round = 0; round < 10; round++)
        {
            for (var frame = 0; frame < 30; frame++)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }

            await Task.Yield();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background, Cancellation);
        }

        view.Settle();
    }

    private static bool Save(ViewScript view, string name)
    {
        var path = Path.Combine(Directory.CreateDirectory(Folder!).FullName, $"{name}.png");
        view.Window.CaptureRenderedFrame()?.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return File.Exists(path);
    }

    private sealed class ScreenAttachments(string scratch) : IJobAttachments
    {
        public string MediaTypeOf(string name) => Path.GetExtension(name) switch
        {
            ".png" => "image/png",
            ".pdf" => "application/pdf",
            _ => "text/csv",
        };

        public ValueTask<Result<Attachment, AttachmentRefusal>> KeepAsync(string name, Stream content, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<Attachment, AttachmentRefusal>.Success(new Attachment(name, MediaTypeOf(name), Path.Combine(scratch, name))));

        public ValueTask<Result<Attachment, AttachmentRefusal>> KeepFileAsync(string path, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<Attachment, AttachmentRefusal>.Success(new Attachment(Path.GetFileName(path), MediaTypeOf(path), path)));
    }
}
