using System.Threading.Channels;
using Avala.Components.UI.Theme;
using Avala.Host.Composition;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Sdk.Events;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Host.Tests;

public sealed class AppearanceTests(HeadlessUi ui, PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutAnAppearanceFileSettingsSayThemeAndMotionFollowTheSystemAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], []);

        var appearance = await AppearanceAsync(run);

        Assert.Equal(
            ("Absent", true, true),
            await run.Ui.ReadAsync(() => (appearance["File"].Text, appearance["FollowsSystem"].Value<bool>(), appearance["MotionFollowsSystem"].Value<bool>())));
        Assert.Equal(new AppearanceSettings(AppearancePreference.Default, AppearanceFileStatus.Absent, Option<AppearanceError>.None), await run.Get<IAppearance>().ReadAsync(Cancellation));
    }

    [Fact]
    public async Task ChoosingLightAndReducedMotionInSettingsIsPublishedWrittenAndKeptAcrossARestartAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], []);
        await run.StartedAsync();
        var appearance = await AppearanceAsync(run);
        var changes = run.Watch<AppearanceChanged>();

        await run.Ui.RunAsync(() => appearance.ExecuteAsync("ChooseThemeCommand", ThemeChoice.Light));
        await run.Ui.RunAsync(() => appearance.ExecuteAsync("ChooseMotionCommand", MotionChoice.Reduced));
        var published = await changes.CollectUntilAsync(changed => changed.Preference.Motion == MotionChoice.Reduced);
        await run.RestartAsync();
        var restarted = await AppearanceAsync(run);

        Assert.Equal([new AppearancePreference(ThemeChoice.Light, MotionChoice.System), new AppearancePreference(ThemeChoice.Light, MotionChoice.Reduced)], published.Select(changed => changed.Preference));
        Assert.Contains("\"theme\": \"light\"", await File.ReadAllTextAsync(Path.Combine(run.DataFolder, "appearance.json"), Cancellation), StringComparison.Ordinal);
        Assert.Equal(
            ("Applied", true, true),
            await run.Ui.ReadAsync(() => (restarted["File"].Text, restarted["IsLight"].Value<bool>(), restarted["IsMotionReduced"].Value<bool>())));
    }

    [Fact]
    public async Task ARejectedAppearanceFileIsReportedAndChoosingAgainRewritesItAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("appearance.json", """{ "theme": "sepia" }""")], []);
        var appearance = await AppearanceAsync(run);
        var rejected = await run.Ui.ReadAsync(() => (appearance["File"].Text, appearance["FollowsSystem"].Value<bool>()));

        await run.Ui.RunAsync(() => appearance.ExecuteAsync("ChooseThemeCommand", ThemeChoice.Dark));

        Assert.Equal(("Rejected: Invalid", true), rejected);
        Assert.Equal(("Applied", true), await run.Ui.ReadAsync(() => (appearance["File"].Text, appearance["IsDark"].Value<bool>())));
    }

    [Fact]
    public async Task TheApplicationAppliesEveryAppearancePublishedOnItsUiThreadAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], []);
        await run.StartedAsync();
        var (application, window, applier) = await CreateAsync();
        var applied = Channel.CreateUnbounded<AppearancePreference>();
        using var following = new CancellationTokenSource();
        var subscription = run.Get<IEventFeed>().SubscribeAsync<AppearanceChanged>(following.Token);
        var follow = applier.FollowAsync(subscription, new Signalling(ui, applier, applied.Writer), following.Token);

        await run.Get<IAppearance>().ChangeAsync(new AppearancePreference(ThemeChoice.Light, MotionChoice.Reduced), Cancellation);
        var light = await applied.Reader.ReadAsync(Cancellation);
        var shown = await ReadOnUiAsync(() => (application.ActualThemeVariant, Motion.GetIsReduced(window)));
        await run.Get<IAppearance>().ChangeAsync(new AppearancePreference(ThemeChoice.Dark, MotionChoice.Full), Cancellation);
        var dark = await applied.Reader.ReadAsync(Cancellation);
        var (variant, reduced) = await ReadOnUiAsync(() => (application.ActualThemeVariant, Motion.GetIsReduced(window)));
        await following.CancelAsync();
        await follow;

        Assert.Equal((new AppearancePreference(ThemeChoice.Light, MotionChoice.Reduced), ThemeVariant.Light, true), (light, shown.ActualThemeVariant, shown.Item2));
        Assert.Equal((new AppearancePreference(ThemeChoice.Dark, MotionChoice.Full), ThemeVariant.Dark, false), (dark, variant, reduced));

        async Task<(Application, Window, AppearanceApplier)> CreateAsync()
        {
            (Application, Window, AppearanceApplier) created = default;
            await ui.RunAsync(
                () =>
                {
                    var application = new Application();
                    var window = new Window();
                    var applier = new AppearanceApplier(application);
                    applier.Attach(window);
                    created = (application, window, applier);
                },
                Cancellation);

            return created;
        }
    }

    [Theory]
    [InlineData(ThemeChoice.System, "Default")]
    [InlineData(ThemeChoice.Light, "Light")]
    [InlineData(ThemeChoice.Dark, "Dark")]
    public void EachThemeChoiceRequestsItsVariant(ThemeChoice theme, string variant) =>
        Assert.Equal(variant, AppearanceApplier.Variant(theme).Key.ToString());

    [Fact]
    public Task AReducedMotionPreferenceReachesTheWindowAttachedLaterAsync() =>
        ui.RunAsync(() =>
        {
            var application = new Application();
            var applier = new AppearanceApplier(application);
            applier.Apply(new AppearancePreference(ThemeChoice.Dark, MotionChoice.Reduced));
            var window = new Window { Content = new Border() };

            applier.Attach(window);

            Assert.Equal((ThemeVariant.Dark, true, true), (application.RequestedThemeVariant, Motion.GetIsReduced(window), Motion.GetIsReduced((Border)window.Content)));
        }, Cancellation);

    [Theory]
    [InlineData(MotionChoice.System, false, false)]
    [InlineData(MotionChoice.System, true, true)]
    [InlineData(MotionChoice.Reduced, false, true)]
    [InlineData(MotionChoice.Full, true, false)]
    public Task ReducedMotionFollowsTheOperatingSystemUnlessTheSettingSaysOnOrOffAsync(MotionChoice motion, bool system, bool reduced) =>
        ui.RunAsync(async () =>
        {
            var applier = new AppearanceApplier(new Application());
            var startup = new AppearanceStartup(new ReadAppearance(new AppearancePreference(ThemeChoice.System, motion)), new FakeSystemMotion(system), applier);

            var window = await startup.ShowAsync(() => new Window(), Cancellation);

            Assert.Equal(reduced, Motion.GetIsReduced(window));
            window.Close();
        }, Cancellation);

    [Fact]
    public Task TheWindowIsCreatedOnlyOnceTheAppearanceIsReadSoItNeverFlashesTheWrongThemeAsync() =>
        ui.RunAsync(async () =>
        {
            var application = new Application();
            var read = new ReadAppearance(new AppearancePreference(ThemeChoice.Light, MotionChoice.Full), new TaskCompletionSource());
            var startup = new AppearanceStartup(read, new FakeSystemMotion(false), new AppearanceApplier(application));
            var created = new List<ThemeVariant?>();

            var showing = startup.ShowAsync(
                () =>
                {
                    created.Add(application.RequestedThemeVariant);

                    return new Window();
                },
                Cancellation);
            var before = created.Count;
            read.Release();
            var window = await showing;

            Assert.Equal(0, before);
            Assert.Equal([ThemeVariant.Light], created);
            Assert.True(window.IsVisible);
            window.Close();
        }, Cancellation);

    [Fact]
    public async Task TheOperatingSystemsReducedMotionIsReadThroughItsOwnToolAsync()
    {
        var processes = new AnsweringProcesses();

        var reduced = await new SystemMotion(processes, () => false).PrefersReducedAsync(Cancellation);

        Assert.True(reduced);
        Assert.Equal(
            OperatingSystem.IsLinux() ? ["gsettings get org.gnome.desktop.interface enable-animations"]
            : OperatingSystem.IsMacOS() ? ["defaults read com.apple.universalaccess reduceMotion"]
            : [],
            processes.Asked);
    }

    [Fact]
    public void WindowsAnswersWhetherItAnimatesWithoutFailing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows has SPI_GETCLIENTAREAANIMATION.");

        var exception = Record.Exception(() => WindowsAnimation.IsOn());

        Assert.Null(exception);
    }

    private sealed class ReadAppearance(AppearancePreference stored, TaskCompletionSource? gate = null) : IAppearance
    {
        public void Release() => gate?.SetResult();

        public async ValueTask<AppearanceSettings> ReadAsync(CancellationToken cancellationToken)
        {
            await (gate?.Task ?? Task.CompletedTask);

            return new AppearanceSettings(stored, AppearanceFileStatus.Applied, Option<AppearanceError>.None);
        }

        public ValueTask<Result<AppearanceSettings, AppearanceError>> ChangeAsync(AppearancePreference preference, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<AppearanceSettings, AppearanceError>.Failure(AppearanceError.Unwritable));
    }

    private sealed class FakeSystemMotion(bool reduced) : ISystemMotion
    {
        public ValueTask<bool> PrefersReducedAsync(CancellationToken cancellationToken) => ValueTask.FromResult(reduced);
    }

    private sealed class AnsweringProcesses : Avala.Sdk.Processes.IProcessRunner
    {
        public List<string> Asked { get; } = [];

        public ValueTask<Result<Avala.Sdk.Processes.ProcessOutcome, Avala.Sdk.Processes.ProcessError>> RunAsync(Avala.Sdk.Processes.ProcessRequest request, CancellationToken cancellationToken)
        {
            Asked.Add(string.Join(' ', [request.FileName, .. request.Arguments]));
            var answer = request.FileName == "gsettings" ? "false\n" : "1\n";

            return ValueTask.FromResult<Result<Avala.Sdk.Processes.ProcessOutcome, Avala.Sdk.Processes.ProcessError>>(new Avala.Sdk.Processes.ProcessOutcome(0, answer, string.Empty));
        }
    }

    private async Task<T> ReadOnUiAsync<T>(Func<T> read)
    {
        T value = default!;
        await ui.RunAsync(() => value = read(), Cancellation);

        return value;
    }

    private static async Task<Bound> AppearanceAsync(SimulatedRun run)
    {
        var settings = run.Page("Settings");
        await run.Ui.RunAsync(() =>
        {
            ((IActivatable)settings.Target).Activate();

            return settings["Loading"].Value<Task>();
        });

        return await run.Ui.ReadAsync(() => settings["Appearance"]);
    }

    private sealed class Signalling(HeadlessUi ui, AppearanceApplier applier, ChannelWriter<AppearancePreference> applied) : IUiDispatcher
    {
        public async ValueTask InvokeAsync(Action action, CancellationToken cancellationToken)
        {
            await ui.RunAsync(action, cancellationToken);
            applied.TryWrite(applier.Applied);
        }
    }
}
