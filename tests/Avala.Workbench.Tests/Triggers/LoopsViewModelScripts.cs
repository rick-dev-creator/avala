using Avala.Autopilot.Contracts;
using Avala.Workbench.Automation;
using Avala.Workbench.Triggers;

namespace Avala.Workbench.Tests.Triggers;

public sealed class LoopsViewModelScripts
{
    private readonly FakeAutopilot autopilot = new();

    [Fact]
    public async Task PauseResumeAndStopActOnTheirLoopOnlyInAStatusTheyFitAsync()
    {
        var loops = Loops();
        var running = loops.Loops.Single(loop => loop.Id == FakeAutopilot.Running);
        var tripped = loops.Loops.Single(loop => loop.Id == FakeAutopilot.Tripped);

        await loops.PauseCommand.ExecuteAsync(running);
        await loops.StopCommand.ExecuteAsync(running);

        Assert.Equal([("pause", FakeAutopilot.Running), ("stop", FakeAutopilot.Running)], autopilot.Commands);
        Assert.Equal(
            (true, false, true, false, false, false),
            (loops.PauseCommand.CanExecute(running), loops.ResumeCommand.CanExecute(running), loops.StopCommand.CanExecute(running),
                loops.PauseCommand.CanExecute(tripped), loops.ResumeCommand.CanExecute(tripped), loops.StopCommand.CanExecute(tripped)));
    }

    [Fact]
    public async Task ARefusedCommandShowsItsErrorAsync()
    {
        var loops = Loops();
        autopilot.Refusal = AutopilotError.LoopEnded;

        await loops.PauseCommand.ExecuteAsync(loops.Loops[0]);

        Assert.Equal("That loop has ended.", loops.Error);
    }

    private LoopsViewModel Loops()
    {
        var loops = new LoopsViewModel(new LoopControls(autopilot));
        loops.Show(autopilot.Digests, TimeZoneInfo.Utc);

        return loops;
    }
}
