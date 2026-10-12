using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.Board;
using Avala.Workbench.Conversation;
using Avala.Workbench.Steering;
using Avala.Workbench.Timeline;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Avala.Workbench.Tests.Views;

public sealed class ComposerChipScreens(HeadlessUi ui)
{
    private const string Gate = "AVALA_FEATURE_SCREENS";

    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch;

    private static string? Folder => Environment.GetEnvironmentVariable(Gate) is { Length: > 0 } found ? found : null;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Shots => new()
    {
        { "dark", "held" },
        { "light", "held" },
        { "dark", "running" },
        { "light", "running" },
    };

    [Theory]
    [MemberData(nameof(Shots))]
    public Task TheComposersChipAndItsMenuAsync(string variant, string state) =>
        ShootAsync(() =>
        {
            var status = state == "held" ? JobStatus.NeedsHelp : JobStatus.Running;
            var view = Show(Conversation(status), variant);
            Save(view, $"composer-chip-{state}-{variant}");
            view.Click("HarnessButton");
            Pump(view);
            Assert.True(Save(view, $"composer-chip-menu-{state}-{variant}"));
        });

    [Theory]
    [InlineData("dark")]
    [InlineData("light")]
    public Task TheWelcomeChipMenuBesideItAsync(string variant) =>
        ShootAsync(() =>
        {
            var view = Screen.Show(new Avala.Workbench.Welcome.DesignWelcomeViewModel(), Theme(variant), 1000, 760);
            view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
            Components.UI.Theme.Motion.SetIsReduced(view.Window, true);
            Pump(view);
            view.Click("HarnessButton");
            Pump(view);
            Assert.True(Save(view, $"composer-welcome-chip-menu-{variant}"));
        });

    private static ConversationViewModel Conversation(JobStatus status)
    {
        var jobs = new FakeJobs { Status = status };
        var connections = new FakeConnections("claude-work", "claude-personal");
        connections.Offers["claude-work"] = CapabilitySet.None.With(new Resumable(), new OffersModels(["opus", "sonnet", "haiku"], ["low", "medium", "high", "xhigh", "max"]) { DefaultModel = "opus", DefaultEffort = "high" });
        var summary = new FakeCatalog().Add("Fix JPY rounding in invoice totals", status).Summary with { Connection = new ConnectionName("claude-work") };
        var items = new TimelineItems(Replying.With(new FakePermissionAnswers()), FakeLinks.Opening, new FakeClipboard(), Tools.Files());
        var composers = new Composers(new JobSteering(jobs, new QueuedMessages(jobs)), new SendKeys(), Composing.Harnesses(jobs, new FakeHandoffRequests(), connections));
        var conversation = new Conversations(composers, items, new TranscriptExport(new FakeClipboard(), new FakeFileSaver(), new Avala.Testing.FakeAppearance())).Open(summary.Job);
        conversation.Show(new BoardJob(summary, Told(status)));

        return conversation;
    }

    private static Transcript Told(JobStatus status)
    {
        var session = SessionId.New();
        var turn = TurnId.New();
        var told = Avala.Workbench.Timeline.Transcript.Empty
            .Apply(new TurnStarted(session, turn), Start)
            .Apply(new ModelReported(session, turn, "claude-opus-5-5") { Effort = "high" }, Start)
            .Apply(new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Message"), Start.AddSeconds(2))
            .Apply(new ItemProgressed(session, turn, new ItemId("reply"), "JPY has no minor units, but `ToMinor` multiplied every currency by 100. I fixed the factor and the invoice totals now round once, in yen."), Start.AddSeconds(3))
            .Apply(new ItemCompleted(session, turn, new ItemId("reply"), ItemOutcome.Succeeded), Start.AddSeconds(4));

        return status == JobStatus.Running ? told : told.Apply(new TurnCompleted(session, turn, TurnOutcome.Interrupted), Start.AddSeconds(5));
    }

    private Task ShootAsync(Action shoot)
    {
        Assert.SkipUnless(Folder is not null && HeadlessApp.Renders, $"Set {Gate} to a folder and AVALA_HEADLESS_RENDER=1 to capture the composer screens.");

        return ui.RunAsync(shoot, Cancellation);
    }

    private static ThemeVariant Theme(string variant) => variant == "light" ? ThemeVariant.Light : ThemeVariant.Dark;

    private static ViewScript Show(ConversationViewModel conversation, string variant)
    {
        var view = Screen.Show(conversation, Theme(variant), 1000, 640);
        view.Window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
        Components.UI.Theme.Motion.SetIsReduced(view.Window, true);
        Pump(view);

        return view;
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
        var path = Path.Combine(Directory.CreateDirectory(Folder!).FullName, $"{name}.png");
        view.Window.CaptureRenderedFrame()?.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return File.Exists(path);
    }
}
