using Avala.Components.Privacy;
using Avala.Sdk.Appearance;
using Avala.Testing;

namespace Avala.Components.Tests.Privacy;

public sealed class PersonalDetailsViewModelScripts
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void PersonalDetailsAreHiddenUntilTheAppearanceRevealsThem()
    {
        using var ui = new TestUiDispatcher();

        ViewModelScript.Given(new PersonalDetailsViewModel(new FakeAppearance(), ui))
            .Then(details => Assert.Equal((true, "Show personal details"), (details.IsHidden, details.Action)));
    }

    [Fact]
    public async Task TogglingRevealsPersonalDetailsAndKeepsTheRestOfTheAppearance()
    {
        using var ui = new TestUiDispatcher();
        var store = new FakeAppearance(new AppearancePreference(ThemeChoice.Dark, MotionChoice.Reduced));
        var details = new PersonalDetailsViewModel(store, ui);

        await details.ToggleCommand.ExecuteAsync(null);

        Assert.Equal((false, "Hide personal details"), (details.IsHidden, details.Action));
        Assert.Equal([new AppearancePreference(ThemeChoice.Dark, MotionChoice.Reduced) { HidesPersonalDetails = false }], store.Changes);
    }

    [Fact]
    public async Task AnAppearanceChangedElsewhereUpdatesTheToggle()
    {
        using var ui = new TestUiDispatcher();
        var details = new PersonalDetailsViewModel(new FakeAppearance(), ui);

        await details.HandleAsync(new AppearanceChanged(AppearancePreference.Default with { HidesPersonalDetails = false }), Cancellation);

        Assert.False(await ui.ReadAsync(() => details.IsHidden));
    }
}
