using System.Globalization;
using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Overview;

namespace Avala.Workbench.Tests.Overview;

public sealed class RelayLegViewModelScripts
{
    private static readonly DateTimeOffset One = new(2026, 10, 25, 13, 0, 0, TimeSpan.Zero);

    private static readonly ConnectionName Work = new("claude-work");

    [Fact]
    public void ALegThatRanSaysHowLongAndWhatEndedIt()
    {
        var mark = new RelayMark(JobId.New(), RelayMarkKind.Capped, Work, One.AddHours(1)) { Why = new LimitReason(Work, "5h", 0.97, 0.9) };
        var leg = new RelayLeg(Work, RelayLegKind.Ran, One, One.AddHours(1).AddMinutes(26)) { Ending = mark };

        var shown = ViewModelScript.Given(new RelayLegViewModel(leg, 0.97, last: false)).ViewModel;

        Assert.Equal(("claude-work", "Ran 1h 26m · capped at 97%", Clock(One), false, false), (shown.Title, shown.Detail, shown.When, shown.IsCurrent, shown.IsWaiting));
    }

    [Fact]
    public void TheRunningLegIsCurrentAndSaysHowMuchOfItsWindowIsUsed()
    {
        var leg = new RelayLeg(Work, RelayLegKind.Running, One, One.AddHours(1));

        var shown = ViewModelScript.Given(new RelayLegViewModel(leg, 0.38, last: true)).ViewModel;

        Assert.Equal(("Running · 38% of its window", true), (shown.Detail, shown.IsCurrent));
    }

    [Fact]
    public void AWaitingLegNamesTheConnectionItWaitsForAndWhenItResumes()
    {
        var leg = new RelayLeg(Work, RelayLegKind.Waiting, One, One.AddHours(1)) { ResumesAt = One.AddHours(4) };

        var shown = ViewModelScript.Given(new RelayLegViewModel(leg, Option<double>.None, last: true)).ViewModel;

        Assert.Equal(("Waiting for claude-work", $"Resumes at {Clock(One.AddHours(4))}", true, true), (shown.Title, shown.Detail, shown.IsCurrent, shown.IsWaiting));
    }

    private static string Clock(DateTimeOffset at) => at.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
}
