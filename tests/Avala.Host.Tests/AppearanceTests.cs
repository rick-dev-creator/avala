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
    public async Task WithoutAnAppearanceFileSettingsSayAvalaFollowsTheSystemWithFullMotionAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], []);

        var appearance = await AppearanceAsync(run);

        Assert.Equal(
            ("Absent", true, false),
            await run.Ui.ReadAsync(() => (appearance["File"].Text, appearance["FollowsSystem"].Value<bool>(), appearance["ReduceMotion"].Value<bool>())));
        Assert.Equal(new AppearanceSettings(AppearancePreference.Default, AppearanceFileStatus.Absent, Option<AppearanceError>.None), await run.Get<IAppearance>().ReadAsync(Cancellation));
    }

    [Fact]
    public async Task ChoosingLightAndReducedMotionInSettingsIsPublishedWrittenAndKeptAcrossARestartAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, [], []);
        var appearance = await AppearanceAsync(run);
        var changes = run.Watch<AppearanceChanged>();

        await run.Ui.RunAsync(() => appearance.ExecuteAsync("ChooseThemeCommand", ThemeChoice.Light));
        await run.Ui.RunAsync(() => appearance.ExecuteAsync("SwitchMotionCommand"));
        var published = await changes.CollectUntilAsync(changed => changed.Preference.ReduceMotion);
        await run.RestartAsync();
        var restarted = await AppearanceAsync(run);

        Assert.Equal([new AppearancePreference(ThemeChoice.Light, false), new AppearancePreference(ThemeChoice.Light, true)], published.Select(changed => changed.Preference));
        Assert.Contains("\"theme\": \"light\"", await File.ReadAllTextAsync(Path.Combine(run.DataFolder, "appearance.json"), Cancellation), StringComparison.Ordinal);
        Assert.Equal(
            ("Applied", true, true),
            await run.Ui.ReadAsync(() => (restarted["File"].Text, restarted["IsLight"].Value<bool>(), restarted["ReduceMotion"].Value<bool>())));
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
        var (application, window, applier) = await CreateAsync();
        var applied = Channel.CreateUnbounded<AppearancePreference>();
        using var following = new CancellationTokenSource();
        var subscription = run.Get<IEventFeed>().SubscribeAsync<AppearanceChanged>(following.Token);
        var follow = applier.FollowAsync(subscription, new Signalling(ui, applier, applied.Writer), following.Token);

        await run.Get<IAppearance>().ChangeAsync(new AppearancePreference(ThemeChoice.Light, true), Cancellation);
        var light = await applied.Reader.ReadAsync(Cancellation);
        var shown = await ReadOnUiAsync(() => (application.ActualThemeVariant, Motion.GetIsReduced(window)));
        await run.Get<IAppearance>().ChangeAsync(new AppearancePreference(ThemeChoice.Dark, false), Cancellation);
        var dark = await applied.Reader.ReadAsync(Cancellation);
        var (variant, reduced) = await ReadOnUiAsync(() => (application.ActualThemeVariant, Motion.GetIsReduced(window)));
        await following.CancelAsync();
        await follow;

        Assert.Equal((new AppearancePreference(ThemeChoice.Light, true), ThemeVariant.Light, true), (light, shown.ActualThemeVariant, shown.Item2));
        Assert.Equal((new AppearancePreference(ThemeChoice.Dark, false), ThemeVariant.Dark, false), (dark, variant, reduced));

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
            applier.Apply(new AppearancePreference(ThemeChoice.Dark, true));
            var window = new Window { Content = new Border() };

            applier.Attach(window);

            Assert.Equal((ThemeVariant.Dark, true, true), (application.RequestedThemeVariant, Motion.GetIsReduced(window), Motion.GetIsReduced((Border)window.Content)));
        }, Cancellation);

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
