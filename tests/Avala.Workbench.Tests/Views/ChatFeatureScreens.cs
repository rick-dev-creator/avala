using Avala.Testing;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Components.UI.Elapsed;
using Avala.Components.UI.Markdown;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing.UI;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Navigation;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workbench.Tests.Views;

public sealed class ChatFeatureScreens(HeadlessUi ui)
{
    private const string Gate = "AVALA_FEATURE_SCREENS";
    private const string Instruction = "Build sistema-solar.html: the sun and the eight planets orbiting it, in one page";
    private const string Reply = """
        Done. **sistema-solar.html** draws the sun and the eight planets on a canvas, each on its own orbit:

        - Orbits scale with the real distances, squeezed so Neptune still fits.
        - Periods follow Kepler's third law, so Mercury laps Neptune 684 times.

        Open it in a browser; nothing else is needed.
        """;

    private const string CodeReply = """
        Each planet advances by its own angular speed, so one function draws a frame:

        ```js
        // Kepler: the period grows with the orbit's radius to the power 1.5
        function step(planet, dt) {
          const speed = (2 * Math.PI) / (planet.period * 365.25);
          planet.angle = (planet.angle + speed * dt) % (2 * Math.PI);
          return { x: Math.cos(planet.angle) * planet.orbit, y: Math.sin(planet.angle) * planet.orbit };
        }
        ```
        """;

    private static readonly string[] PageLines =
    [
        "<!doctype html>",
        "<html lang=\"es\">",
        "<head><meta charset=\"utf-8\"><title>Sistema solar</title></head>",
        "<body>",
        "  <canvas id=\"orbits\" width=\"900\" height=\"900\"></canvas>",
        "  <script src=\"orbits.js\"></script>",
        "</body>",
        "</html>",
    ];

    private static readonly (double Radius, double Angle, double Size, string Color)[] Planets =
    [
        (48, 0.6, 3, "#B7B0A8"),
        (74, 2.4, 5, "#E8C27A"),
        (102, 4.1, 5.5, "#5B9BE8"),
        (128, 5.6, 4, "#D9643A"),
        (182, 1.2, 12, "#D8B48A"),
        (226, 3.5, 10, "#E6D3A0"),
        (262, 0.2, 7, "#9FD8E0"),
        (296, 2.0, 7, "#5D7BE0"),
    ];

    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;
    private readonly SessionId session = SessionId.New();
    private readonly TurnId turn = TurnId.New();

    private static string? Folder => Environment.GetEnvironmentVariable(Gate) is { Length: > 0 } found ? found : null;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Variants => new("dark", "light");

    [Theory]
    [MemberData(nameof(Variants))]
    public Task HoverBarsUnderAReplyAndUnderAPromptAsync(string variant) =>
        ShootAsync(() =>
        {
            var conversation = Conversation(Finished(), JobStatus.NeedsHelp);
            var view = Show(conversation, variant);
            Hover(view, Last(view, "MessageView"));
            Save(view, $"01-hover-reply-{variant}");
            view.Click(Last(view, "MessageView").GetVisualDescendants().OfType<Control>().First(control => control.Name == "Copy"));
            Pump(view);
            Save(view, $"02-hover-reply-copied-{variant}");
            Hover(view, First(view, "PromptView"));
            Assert.True(Save(view, $"03-hover-prompt-{variant}"));
        });

    [Theory]
    [MemberData(nameof(Variants))]
    public Task TheContextMenuOfAReplyAsync(string variant) =>
        ShootAsync(() =>
        {
            var view = Show(Conversation(Finished(), JobStatus.NeedsHelp), variant);
            var text = view.All<MarkdownText>()[^1];
            text.SelectAll();
            Hover(view, text);
            ((PopupFlyoutBase)text.ContextFlyout!).ShowAt(text, showAtPointer: true);
            Pump(view);
            Assert.True(Save(view, $"04-context-menu-{variant}"));
        });

    [Theory]
    [MemberData(nameof(Variants))]
    public Task TheWorkingRowMidWriteAndTheStopButtonAsync(string variant) =>
        ShootAsync(() =>
        {
            var view = Show(Conversation(Writing(), JobStatus.Running), variant);
            ElapsedText.SetClock(view.Window, new FakeTimeProvider(Start.AddSeconds(23)));
            Pump(view);
            Save(view, $"05-working-mid-write-{variant}");
            var thinking = Show(Conversation(Begun(), JobStatus.Running), variant);
            ElapsedText.SetClock(thinking.Window, new FakeTimeProvider(Start.AddSeconds(23)));
            Pump(thinking);
            Assert.True(Save(thinking, $"05-working-thinking-{variant}"));
        });

    [Theory]
    [MemberData(nameof(Variants))]
    public Task JumpToLatestAsync(string variant) =>
        ShootAsync(() =>
        {
            var conversation = Conversation(Finished(), JobStatus.Running);
            var view = Show(conversation, variant, 440);
            ElapsedText.SetClock(view.Window, new FakeTimeProvider(Start.AddSeconds(41)));
            var scroller = view.Find<ScrollViewer>("Scroller");
            scroller.Offset = scroller.Offset.WithY(0);
            Pump(view);
            var next = TurnId.New();
            conversation.Show(Board(
                Finished().Apply(new TurnStarted(session, next), Start.AddSeconds(36))
                    .Apply(new ItemStarted(session, next, new ItemId("more"), ItemKind.Message, "Message"), Start.AddSeconds(37))
                    .Apply(new ItemProgressed(session, next, new ItemId("more"), "Adding the moons next."), Start.AddSeconds(37))
                    .Apply(new ItemCompleted(session, next, new ItemId("more"), ItemOutcome.Succeeded), Start.AddSeconds(38)),
                JobStatus.Running));
            Pump(view);
            Assert.True(Save(view, $"06-jump-to-latest-{variant}"));
        });

    [Theory]
    [MemberData(nameof(Variants))]
    public Task TheHeaderMoreMenuAndTheLiveSidebarRowAsync(string variant) =>
        ShootAsync(() =>
        {
            var view = Screen.Show(new DesignWorkbenchViewModel(), Theme(variant), 1100, 520);
            Components.UI.Theme.Motion.SetIsReduced(view.Window, true);
            view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
            view.Find<Button>("More").Flyout!.ShowAt(view.Find<Button>("More"));
            Pump(view);
            Save(view, $"07-more-menu-{variant}");
            var sidebar = Screen.Show(new DesignSidebarViewModel(), Theme(variant), 360, 420);
            Pump(sidebar);
            Assert.True(Save(sidebar, $"08-sidebar-live-row-{variant}"));
        });

    [Theory]
    [MemberData(nameof(Variants))]
    public Task AHighlightedCodeBlockWithItsHoverCopyAsync(string variant) =>
        ShootAsync(() =>
        {
            var view = Show(Conversation(Coded(), JobStatus.NeedsHelp), variant);
            var block = view.All<Border>().Last(border => border.Classes.Contains("markdown-code-block"));
            Hover(view, block);
            Assert.True(Save(view, $"09-code-block-{variant}"));
        });

    [Theory]
    [MemberData(nameof(Variants))]
    public Task AFileEditsHunksInAPopoverAsync(string variant) =>
        ShootAsync(() =>
        {
            var changes = new FakeChanges { Hunks = { ["sistema-solar.html"] = Page() } };
            var view = Show(Conversation(Finished(), JobStatus.NeedsHelp, changes), variant);
            var line = view.All<Button>().Last(button => button.Name == "Toggle" && button.FindAncestorOfType<UserControl>()?.GetType().Name == "ToolView");
            view.Click(line);
            Pump(view);
            Assert.True(Save(view, $"10-file-edit-diff-{variant}"));
        });

    [Theory]
    [MemberData(nameof(Variants))]
    public Task AWrittenPageOffersItsPreviewOnItsLineAsync(string variant) =>
        ShootAsync(() =>
        {
            var files = new FakeWorkingFiles { Stored = { ["/repo/sistema-solar.html"] = [60] } };
            var view = Show(Conversation(Finished(), JobStatus.NeedsHelp, new FakeChanges(), files), variant);
            var preview = view.All<Button>().Single(button => button.Name == "OpenPreview" && button.IsEffectivelyVisible);
            Hover(view, preview);
            Assert.True(Save(view, $"11-html-line-preview-{variant}"));
        });

    [Theory]
    [MemberData(nameof(Variants))]
    public Task AWrittenImageShowsItsThumbnailAndOpensInASheetAsync(string variant) =>
        ShootAsync(() =>
        {
            var files = new FakeWorkingFiles { Stored = { ["/repo/orbits.png"] = Orbits() } };
            var view = Show(Conversation(Drawn(), JobStatus.NeedsHelp, new FakeChanges(), files), variant);
            Pump(view);
            Save(view, $"12-image-thumbnail-{variant}");
            view.Click(view.All<Button>().Single(button => button.Name == "OpenPreview" && button.IsEffectivelyVisible));
            Pump(view);
            Assert.True(Save(view, $"13-image-preview-sheet-{variant}"));
        });

    internal static byte[] Orbits()
    {
        var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(720, 420));

        using (var context = bitmap.CreateDrawingContext())
        {
            var center = new Point(360, 210);
            context.FillRectangle(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0E1222")), new Rect(0, 0, 720, 420));
            var orbit = new Avalonia.Media.Pen(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3DFFFFFF")), 1);

            foreach (var (radius, angle, size, color) in Planets)
            {
                context.DrawEllipse(null, orbit, center, radius, radius * 0.5);
                var at = new Point(center.X + (radius * Math.Cos(angle)), center.Y + (radius * 0.5 * Math.Sin(angle)));
                context.DrawEllipse(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(color)), null, at, size, size);
            }

            context.DrawEllipse(new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F5B942")), null, center, 26, 26);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return stream.ToArray();
    }

    private Task ShootAsync(Action shoot)
    {
        Assert.SkipUnless(Folder is not null && HeadlessApp.Renders, $"Set {Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the chat screens.");

        return ui.RunAsync(shoot, Cancellation);
    }

    private static UserControl Last(ViewScript view, string type) => view.All<UserControl>().Last(control => control.GetType().Name == type);

    private static UserControl First(ViewScript view, string type) => view.All<UserControl>().First(control => control.GetType().Name == type);

    private static ThemeVariant Theme(string variant) => variant == "light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private static ViewScript Show(ConversationViewModel conversation, string variant, double height = 760)
    {
        var view = Screen.Show(conversation, Theme(variant), 1000, height);
        view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
        Components.UI.Theme.Motion.SetIsReduced(view.Window, true);
        Pump(view);

        return view;
    }

    private static void Hover(ViewScript view, Control target)
    {
        var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 3), view.Window) ?? default;
        view.Window.MouseMove(center);
        Pump(view);
    }

    private static void Pump(ViewScript view)
    {
        for (var frame = 0; frame < 30; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }

        view.Settle();
    }

    private static bool Save(ViewScript view, string name)
    {
        var path = Path.Combine(Directory.CreateDirectory(Folder!).FullName, $"chat-{name}.png");
        view.Window.CaptureRenderedFrame()?.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return File.Exists(path);
    }

    private static ConversationViewModel Conversation(Transcript transcript, JobStatus status) => Conversation(transcript, status, new FakeChanges());

    private static ConversationViewModel Conversation(Transcript transcript, JobStatus status, FakeChanges changes) => Conversation(transcript, status, changes, new FakeWorkingFiles());

    private static ConversationViewModel Conversation(Transcript transcript, JobStatus status, FakeChanges changes, FakeWorkingFiles files)
    {
        var jobs = new FakeJobs { Status = status };
        var catalog = new FakeCatalog();
        var job = catalog.Add(Instruction, status).Summary.Job;
        catalog.Change(job, history => history with { Summary = history.Summary with { Workspace = Avala.Workspaces.Contracts.WorkspaceId.New() } });
        var items = new TimelineItems(Replying.With(new FakePermissionAnswers()), new FakeClipboard(), Tools.Viewing(new Avala.Workbench.Reviewing.WorkedFiles(catalog, changes, new FakeWorkspaces(), files)));
        var conversation = new Conversations(Composing.Of(new JobSteering(jobs, new QueuedMessages(jobs, Avala.Workbench.Tests.Composing.Drafts(), new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger())), new SendKeys()), items, new TranscriptExport(new FakeClipboard(), new FakeFileSaver(), new FakeAppearance())).Open(job);
        conversation.Show(Board(transcript, status));

        return conversation;
    }

    private static BoardJob Board(Transcript transcript, JobStatus status) =>
        new(new FakeCatalog().Add(Instruction, status).Summary, transcript);

    private Transcript Coded() =>
        Finished()
            .Apply(new TurnStarted(session, TurnId.New()), Start.AddSeconds(40))
            .Apply(new ItemStarted(session, turn, new ItemId("code"), ItemKind.Message, "Message"), Start.AddSeconds(41))
            .Apply(new ItemProgressed(session, turn, new ItemId("code"), CodeReply), Start.AddSeconds(41))
            .Apply(new ItemCompleted(session, turn, new ItemId("code"), ItemOutcome.Succeeded), Start.AddSeconds(42));

    private static Avala.Workspaces.Contracts.FileDiff Page() =>
        new("sistema-solar.html", false, [new Avala.Workspaces.Contracts.DiffHunk(0, 0, 1, 9, string.Empty, [.. PageLines.Select(line => new Avala.Workspaces.Contracts.DiffLine(Avala.Workspaces.Contracts.DiffLineKind.Added, line))])]);

    private Transcript Drawn() =>
        Begun()
            .Apply(new ItemStarted(session, turn, new ItemId("png"), ItemKind.FileEdit, "Write orbits.png") { Input = "PNG" }, Start.AddSeconds(30))
            .Apply(new ItemCompleted(session, turn, new ItemId("png"), ItemOutcome.Succeeded), Start.AddSeconds(31))
            .Apply(new ItemStarted(session, turn, new ItemId("drawn"), ItemKind.Message, "Message"), Start.AddSeconds(32))
            .Apply(new ItemProgressed(session, turn, new ItemId("drawn"), "Drew **orbits.png**: the sun and the eight planets on their orbits, to scale by order."), Start.AddSeconds(33))
            .Apply(new ItemCompleted(session, turn, new ItemId("drawn"), ItemOutcome.Succeeded), Start.AddSeconds(34))
            .Apply(new TurnCompleted(session, turn, TurnOutcome.Finished), Start.AddSeconds(35));

    private Transcript Begun() =>
        Transcript.Empty.WithPrompts(Instruction, [])
            .Apply(new TurnStarted(session, turn), Start)
            .Apply(new ItemStarted(session, turn, new ItemId("think"), ItemKind.Reasoning, "Thinking"), Start)
            .Apply(new ItemProgressed(session, turn, new ItemId("think"), "One self-contained page: a canvas, requestAnimationFrame, and real orbital ratios."), Start)
            .Apply(new ItemCompleted(session, turn, new ItemId("think"), ItemOutcome.Succeeded), Start.AddSeconds(4))
            .Apply(new ItemStarted(session, turn, new ItemId("look"), ItemKind.Search, "Search *.html") { Input = "*.html" }, Start.AddSeconds(5))
            .Apply(new ItemProgressed(session, turn, new ItemId("look"), "No files found"), Start.AddSeconds(5))
            .Apply(new ItemCompleted(session, turn, new ItemId("look"), ItemOutcome.Succeeded), Start.AddSeconds(5))
            .Apply(new ItemStarted(session, turn, new ItemId("plan"), ItemKind.Message, "Message"), Start.AddSeconds(6))
            .Apply(new ItemProgressed(session, turn, new ItemId("plan"), "The folder is empty, so I'll write the whole page in one file."), Start.AddSeconds(6))
            .Apply(new ItemCompleted(session, turn, new ItemId("plan"), ItemOutcome.Succeeded), Start.AddSeconds(7));

    private Transcript Writing() =>
        Begun().Apply(new ItemDrafted(session, turn, new ItemId("write"), ItemKind.FileEdit, "Write sistema-solar.html", 4300), Start.AddSeconds(20));

    private Transcript Finished() =>
        Begun()
            .Apply(new ItemStarted(session, turn, new ItemId("write"), ItemKind.FileEdit, "Write sistema-solar.html") { Input = "<!doctype html>" }, Start.AddSeconds(30))
            .Apply(new ItemCompleted(session, turn, new ItemId("write"), ItemOutcome.Succeeded), Start.AddSeconds(31))
            .Apply(new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Message"), Start.AddSeconds(32))
            .Apply(new ItemProgressed(session, turn, new ItemId("reply"), Reply), Start.AddSeconds(33))
            .Apply(new ItemCompleted(session, turn, new ItemId("reply"), ItemOutcome.Succeeded), Start.AddSeconds(34))
            .Apply(new TurnCompleted(session, turn, TurnOutcome.Finished), Start.AddSeconds(35));
}
