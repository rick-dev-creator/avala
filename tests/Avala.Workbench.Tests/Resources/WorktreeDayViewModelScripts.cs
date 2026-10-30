using Avala.Resources.Contracts;
using Avala.Workbench.Resources;

namespace Avala.Workbench.Tests.Resources;

public sealed class WorktreeDayViewModelScripts
{
    [Fact]
    public void ADayShowsItsPeakAgainstTheBusiestAndWhatHappenedThatDay()
    {
        var day = new WorktreeDayViewModel(new WorktreeDay(new DateOnly(2026, 10, 24), 1L << 30, 3, 2, 1, 512L << 20), 2L << 30, isToday: false);

        Assert.Equal(("Sat 24 Oct", 0.5, "1 GB", "2 created · 1 freed · 512 MB recovered", false), (day.Day, day.Share, day.Peak, day.Detail, day.IsToday));
    }
}
