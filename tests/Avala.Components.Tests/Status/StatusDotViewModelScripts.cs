using Avala.Components.Status;
using Avala.Testing;

namespace Avala.Components.Tests.Status;

public sealed class StatusDotViewModelScripts
{
    [Fact]
    public void ADotThatStopsWorkingAndIsHeldTellsItsView() =>
        ViewModelScript.Given(new StatusDotViewModel(StatusKind.Working))
            .When(dot => dot.Kind = StatusKind.Held)
            .ThenNotified(nameof(StatusDotViewModel.Kind))
            .Then(dot => Assert.Equal(StatusKind.Held, dot.Kind));

    [Fact]
    public void TheDesignTimeDotIsWorking() =>
        Assert.Equal(StatusKind.Working, new DesignStatusDotViewModel().Kind);
}
