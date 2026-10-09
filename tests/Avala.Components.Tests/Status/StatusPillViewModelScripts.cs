using Avala.Components.Status;
using Avala.Testing;

namespace Avala.Components.Tests.Status;

public sealed class StatusPillViewModelScripts
{
    [Fact]
    public void APillShowsItsStateWithAMatchingDot() =>
        ViewModelScript.Given(new StatusPillViewModel(StatusKind.NeedsYou, "Needs you"))
            .Then(pill =>
            {
                Assert.Equal("Needs you", pill.Text);
                Assert.Equal(StatusKind.NeedsYou, pill.Dot.Kind);
            });

    [Fact]
    public void ChangingTheStateChangesTheDotAndTheText() =>
        ViewModelScript.Given(new StatusPillViewModel(StatusKind.Working, "Running"))
            .When(pill =>
            {
                pill.Kind = StatusKind.Held;
                pill.Text = "Held · stalled";
            })
            .ThenNotified(nameof(StatusPillViewModel.Kind), nameof(StatusPillViewModel.Text))
            .Then(pill => Assert.Equal(StatusKind.Held, pill.Dot.Kind));

    [Fact]
    public void TheDesignTimePillIsHeldAndStalled()
    {
        var pill = new DesignStatusPillViewModel();

        Assert.Equal(("Held · stalled", StatusKind.Held), (pill.Text, pill.Dot.Kind));
    }
}
