using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Tests.Capabilities;

public sealed class CapabilitySetTests
{
    [Fact]
    public void AnAttachedComponentIsFoundByItsTypeAndAnAbsentOneIsNone()
    {
        var declared = CapabilitySet.Of(new Resumable(), new ReportsCost("USD"));

        Assert.Equal(Option<ReportsCost>.Some(new ReportsCost("USD")), declared.Get<ReportsCost>());
        Assert.Equal(Option<Interruptible>.None, declared.Get<Interruptible>());
        Assert.Equal((true, false), (declared.Has<Resumable>(), declared.Has<AsksForms>()));
    }

    [Fact]
    public void AttachingAComponentOfATypeAlreadyPresentRefinesItAndRemovingItLeavesTheOthers()
    {
        var subscription = CapabilitySet.Of(new Resumable(), new ReportsLimits(["5h"]));

        var refined = subscription.With(new ReportsLimits(["5h", "7d"]));

        Assert.Equal(Option<ReportsLimits>.Some(new ReportsLimits(["7d", "5h"])), refined.Get<ReportsLimits>());
        Assert.Equal(CapabilitySet.Of(new Resumable()), refined.Without<ReportsLimits>());
        Assert.Equal(Option<ReportsLimits>.Some(new ReportsLimits(["5h"])), subscription.Get<ReportsLimits>());
    }

    [Fact]
    public void SetsWithTheSameComponentsAreEqualWhateverTheOrderTheyWereAttachedIn()
    {
        var one = CapabilitySet.Of(new AcceptsTools([ToolSurface.Canvas, ToolSurface.Executed]), new AsksForms());
        var other = CapabilitySet.Of(new AsksForms()).With(new AcceptsTools([ToolSurface.Executed, ToolSurface.Canvas]));

        Assert.Equal(one, other);
        Assert.Equal(one.GetHashCode(), other.GetHashCode());
        Assert.NotEqual(one, other.With(new AcceptsTools([ToolSurface.Executed])));
    }

    [Fact]
    public void APluginDefinesItsOwnComponentWithoutChangingTheContract()
    {
        var declared = CapabilitySet.Of(new Resumable()).With(new SpeaksAloud("calm"));

        Assert.Equal(Option<SpeaksAloud>.Some(new SpeaksAloud("calm")), declared.Get<SpeaksAloud>());
        Assert.Equal(["Resumable", "SpeaksAloud"], declared.Components.Select(component => component.GetType().Name));
    }

    private sealed record SpeaksAloud(string Voice) : ICapability;
}
