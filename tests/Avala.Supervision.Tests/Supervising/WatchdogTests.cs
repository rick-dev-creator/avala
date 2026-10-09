using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Testing;
using static Avala.Supervision.Tests.Supervising.Supervised;

namespace Avala.Supervision.Tests.Supervising;

public sealed class WatchdogTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task ARunningJobSilentForTheWholeWindowIsHeldAsStalledWithTheSilenceMeasuredAsync()
    {
        await using var supervised = new Supervised();
        await supervised.RunningAsync();

        await supervised.AdvanceAsync(Window);

        Assert.Equal([(supervised.Job, HoldReason.Stalled)], supervised.Jobs.Holds);
        var intervention = Assert.Single(supervised.Interventions);
        Assert.Equal(
            (HoldReason.Stalled, new SilenceMeasure(Window, Window), supervised.Clock.GetUtcNow()),
            (intervention.Hold.Reason, intervention.Silence, intervention.At));
        Assert.Equal(new SupervisorIntervened(intervention), supervised.Bus.Published[^1]);
    }

    [Fact]
    public async Task ActivityRestartsTheWindowFromTheLastEventAsync()
    {
        await using var supervised = new Supervised();
        await supervised.RunningAsync();
        await supervised.AdvanceAsync(Window - Minute);
        await supervised.SeeAsync(new ItemStarted(supervised.Session, supervised.Turn, new ItemId("build"), ItemKind.Command, "Build"));

        await supervised.AdvanceAsync(Minute);
        Assert.Empty(supervised.Jobs.Holds);

        await supervised.AdvanceAsync(Window - Minute);
        Assert.Equal(new SilenceMeasure(Window, Window), Assert.Single(supervised.Interventions).Silence);
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("form")]
    [InlineData("call")]
    public async Task WaitingForAPermissionAFormOrAToolCallToBeAnsweredIsNeverSilenceAsync(string waitingFor)
    {
        await using var supervised = new Supervised();
        await supervised.RunningAsync();
        var item = new ItemId("migrate");
        var question = new AgentForm(FormPurpose.Question, "Migrate?", "", [new FormField("go", "Go", "Go on?", FieldKind.Confirmation, [])]);
        await supervised.SeeAsync(waitingFor switch
        {
            "form" => new FormRequested(supervised.Session, supervised.Turn, item, question),
            "call" => new ToolCalled(supervised.Session, supervised.Turn, item, "delegate", "{}"),
            _ => new PermissionRequested(supervised.Session, supervised.Turn, item, "Run", ItemKind.Command, "dotnet ef"),
        });

        await supervised.AdvanceAsync(Window * 10);
        Assert.Empty(supervised.Jobs.Holds);

        await supervised.SeeAsync(waitingFor switch
        {
            "form" => new FormAnswered(supervised.Session, supervised.Turn, item, new FormAnswer(item, [new FieldAnswer("go") { Confirmed = true }])),
            "call" => new ToolReturned(supervised.Session, supervised.Turn, item, new ToolResult(item, "Done")),
            _ => new PermissionResolved(supervised.Session, supervised.Turn, item, PermissionAnswer.Allow),
        });
        await supervised.AdvanceAsync(Window);
        Assert.Single(supervised.Jobs.Holds);
    }

    [Fact]
    public async Task AJobWaitingForSeveralToolCallsStaysAwakeUntilTheLastOneIsAnsweredAsync()
    {
        await using var supervised = new Supervised();
        await supervised.RunningAsync();
        var first = new ItemId("first");
        var second = new ItemId("second");
        await supervised.SeeAsync(new ToolCalled(supervised.Session, supervised.Turn, first, "delegate", "{}"));
        await supervised.SeeAsync(new ToolCalled(supervised.Session, supervised.Turn, second, "delegate", "{}"));

        await supervised.SeeAsync(new ToolReturned(supervised.Session, supervised.Turn, first, new ToolResult(first, "Done")));
        await supervised.AdvanceAsync(Window * 3);
        Assert.Empty(supervised.Jobs.Holds);

        await supervised.SeeAsync(new ItemCompleted(supervised.Session, supervised.Turn, second, ItemOutcome.Abandoned));
        await supervised.AdvanceAsync(Window);
        Assert.Single(supervised.Jobs.Holds);
    }

    [Fact]
    public async Task AJobThatIsNotRunningIsNeverHeldForSilenceAsync()
    {
        await using var supervised = new Supervised();
        await supervised.RunningAsync();

        await supervised.ProgressAsync(JobStatus.Checking);
        await supervised.AdvanceAsync(Window * 2);

        Assert.Empty(supervised.Jobs.Holds);
    }

    [Fact]
    public async Task OnlyActivityOfTheJobsCurrentSessionKeepsItAwakeAsync()
    {
        await using var supervised = new Supervised();
        var replaced = SessionId.New();
        await supervised.JoinAsync(replaced);
        await supervised.RunningAsync();
        await supervised.AdvanceAsync(Window - Minute);

        await supervised.SeeAsync(new TurnStarted(replaced, TurnId.New()));
        await supervised.AdvanceAsync(Minute);

        Assert.Single(supervised.Jobs.Holds);
    }

    [Fact]
    public async Task AHoldJobsRejectsIsNotRecordedAsAnInterventionAsync()
    {
        await using var supervised = new Supervised(JobRejection.NotRunning);
        await supervised.RunningAsync();

        await supervised.AdvanceAsync(Window);

        Assert.Single(supervised.Jobs.Holds);
        Assert.Empty(supervised.Interventions);
        Assert.DoesNotContain(supervised.Bus.Published, published => published is SupervisorIntervened);
    }
}
