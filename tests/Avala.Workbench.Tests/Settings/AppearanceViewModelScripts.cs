using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Testing;
using Avala.Workbench.Settings;

namespace Avala.Workbench.Tests.Settings;

public sealed class AppearanceViewModelScripts
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutAFileThemeAndMotionFollowTheSystem()
    {
        var appearance = new AppearanceViewModel(new FakeAppearance());

        await appearance.LoadAsync(Cancellation);

        Assert.Equal((true, false, false, true, "Absent"), (appearance.FollowsSystem, appearance.IsLight, appearance.IsDark, appearance.MotionFollowsSystem, appearance.File));
    }

    [Fact]
    public async Task ChoosingAThemeKeepsItAndShowsItChosen()
    {
        var store = new FakeAppearance();
        var appearance = new AppearanceViewModel(store);
        await appearance.LoadAsync(Cancellation);

        ViewModelScript.Given(appearance)
            .Invoke("ChooseThemeCommand", ThemeChoice.Light)
            .ThenNotified(nameof(AppearanceViewModel.FollowsSystem), nameof(AppearanceViewModel.IsLight), nameof(AppearanceViewModel.File))
            .Then(page => Assert.Equal((false, true, false, "Applied"), (page.FollowsSystem, page.IsLight, page.IsDark, page.File)));

        Assert.Equal([new AppearancePreference(ThemeChoice.Light, MotionChoice.System)], store.Changes);
    }

    [Fact]
    public async Task ChoosingReducedMotionOnOffOrSystemKeepsTheTheme()
    {
        var store = new FakeAppearance(new AppearancePreference(ThemeChoice.Dark, MotionChoice.System));
        var appearance = new AppearanceViewModel(store);
        await appearance.LoadAsync(Cancellation);

        await appearance.ChooseMotionCommand.ExecuteAsync(MotionChoice.Reduced);
        var reduced = (appearance.IsMotionReduced, appearance.MotionFollowsSystem);
        await appearance.ChooseMotionCommand.ExecuteAsync(MotionChoice.Full);
        var full = appearance.IsMotionFull;
        await appearance.ChooseMotionCommand.ExecuteAsync(MotionChoice.System);

        Assert.Equal(
            [new AppearancePreference(ThemeChoice.Dark, MotionChoice.Reduced), new AppearancePreference(ThemeChoice.Dark, MotionChoice.Full), new AppearancePreference(ThemeChoice.Dark, MotionChoice.System)],
            store.Changes);
        Assert.Equal(((true, false), true, true, true), (reduced, full, appearance.MotionFollowsSystem, appearance.IsDark));
    }

    [Fact]
    public async Task AChangeThatCannotBeWrittenSaysSoAndKeepsTheShownAppearance()
    {
        var appearance = new AppearanceViewModel(new FakeAppearance { Refusal = AppearanceError.Unwritable });
        await appearance.LoadAsync(Cancellation);

        await appearance.ChooseThemeCommand.ExecuteAsync(ThemeChoice.Dark);

        Assert.Equal((true, "appearance.json could not be written, so the change was not kept."), (appearance.FollowsSystem, appearance.Error));
    }

    [Fact]
    public async Task ARejectedFileSaysWhyAndShowsTheDefaults()
    {
        var appearance = new AppearanceViewModel(new FakeAppearance { File = AppearanceFileStatus.Rejected, Fault = AppearanceError.Invalid });

        await appearance.LoadAsync(Cancellation);

        Assert.Equal(("Rejected: Invalid", true), (appearance.File, appearance.FollowsSystem));
    }
}
