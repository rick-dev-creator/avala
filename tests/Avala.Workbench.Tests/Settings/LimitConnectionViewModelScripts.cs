using Avala.Testing;
using Avala.Workbench.Settings;

namespace Avala.Workbench.Tests.Settings;

public sealed class LimitConnectionViewModelScripts
{
    [Fact]
    public void TheFirstConnectionCannotMoveUpAndTheOthersCan()
    {
        var first = new LimitConnectionViewModel("claude-work", true, _ => { }, () => { });
        var second = new LimitConnectionViewModel("claude-max", false, _ => { }, () => { });

        first.Place(0);

        ViewModelScript.Given(second)
            .When(row => row.Place(1))
            .ThenNotified(nameof(ILimitConnectionViewModel.Position), nameof(ILimitConnectionViewModel.CanMoveUp))
            .Then(row => Assert.Equal(("1", false, "2", true), (first.Position, first.CanMoveUp, row.Position, row.CanMoveUp)));
    }

    [Fact]
    public void ChoosingAConnectionTellsTheSectionAndMovingItUpAsksForIt()
    {
        var changed = 0;
        var moved = new List<LimitConnectionViewModel>();
        var row = new LimitConnectionViewModel("claude-max", false, moved.Add, () => changed++);

        row.IsIncluded = true;
        row.MoveUpCommand.Execute(null);

        Assert.Equal((1, 1), (changed, moved.Count));
    }
}
