using Avala.Autopilot.Contracts;
using Avala.Autopilot.RepositoryFiles;
using Avala.Autopilot.Sourcing;
using Avala.Autopilot.Tests.Looping;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Autopilot.Tests.Sourcing;

public sealed class SourceTests
{
    private static readonly string Repository = Pilot.Repository;

    private static readonly DateTimeOffset Now = new(2026, 10, 9, 22, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheBacklogOffersItsFirstTaskNotYetTakenFromTheFileOfTheCurrentBaseEachTimeAsync()
    {
        var files = new CommittedFiles().With(Repository, BacklogFileReader.BacklogFile, Backlog(("greet", "Greet the team")));
        var ledger = new MemoryLedger();
        var source = new BacklogSource(new BacklogFileReader(files), ledger);

        var first = await NextTaskAsync(source);
        await source.MarkAsync(first, new TaskMark(TaskState.Taken, JobId.New(), Now), Cancellation);
        var drained = Outcomes.Succeeds(await source.NextAsync(Request(Now), Cancellation));
        files.With(Repository, BacklogFileReader.BacklogFile, Backlog(("greet", "Greet the team"), ("docs", "Document the greeting")));
        var added = await NextTaskAsync(source);

        Assert.Equal(new SourcedTask(BacklogSource.Source, "greet", Repository, "Greet the team"), first);
        Assert.Equal(SourceAnswer.Nothing, drained);
        Assert.Equal("docs", added.Key);
    }

    [Fact]
    public async Task ARecurringTaskIsDueAtOnceThenEveryIntervalAfterItWasLastTakenAsync()
    {
        var files = new CommittedFiles().With(Repository, BacklogFileReader.BacklogFile, """{ "recurring": [ { "id": "deps", "instruction": "Update the dependencies", "everyMinutes": 60 } ] }""");
        var source = new RecurringSource(new BacklogFileReader(files), new MemoryLedger());

        var first = await NextTaskAsync(source);
        await source.MarkAsync(first, new TaskMark(TaskState.Taken, JobId.New(), Now), Cancellation);
        await source.MarkAsync(first, new TaskMark(TaskState.Approved, JobId.New(), Now.AddMinutes(5)), Cancellation);
        var waiting = Outcomes.Succeeds(await source.NextAsync(Request(Now.AddMinutes(59)), Cancellation));
        var again = await NextTaskAsync(source, Now.AddMinutes(60));

        Assert.Equal(new SourcedTask(RecurringSource.Source, "deps", Repository, "Update the dependencies"), first);
        Assert.Equal(SourceAnswer.Nothing with { NextDue = Now.AddMinutes(60) }, waiting);
        Assert.Equal(first, again);
    }

    [Fact]
    public async Task FollowUpsAreOfferedOldestFirstUntilTakenAsync()
    {
        var ledger = new MemoryLedger();
        var source = new FollowUpSource(ledger);
        var older = new SourcedTask(FollowUpSource.Source, "a", Repository, "Announce the changelog");
        var newer = new SourcedTask(FollowUpSource.Source, "b", Repository, "Tag a release");
        await ledger.ProposeAsync(newer, Now.AddMinutes(1), Cancellation);
        await ledger.ProposeAsync(older, Now, Cancellation);

        var first = await NextTaskAsync(source);
        await source.MarkAsync(first, new TaskMark(TaskState.Taken, JobId.New(), Now.AddMinutes(2)), Cancellation);

        Assert.Equal((older, newer), (first, await NextTaskAsync(source)));
    }

    [Fact]
    public async Task SourcesAreAskedInOrderAndWithoutATaskTheEarliestDueTimeIsKeptAsync()
    {
        var empty = new MemorySource("empty") { NextDue = Now.AddHours(2) };
        var later = new MemorySource("later") { NextDue = Now.AddHours(1) };
        var sources = new TaskSources([empty, later]);

        var waiting = Outcomes.Succeeds(await sources.NextAsync(Request(Now), Cancellation));
        var task = later.Add("deps", "Update the dependencies");
        var offered = Outcomes.Succeeds(await sources.NextAsync(Request(Now), Cancellation));
        await sources.MarkAsync(task, new TaskMark(TaskState.Taken, JobId.New(), Now), Cancellation);
        empty.Error = AutopilotError.Malformed;

        Assert.Equal(Option<DateTimeOffset>.Some(Now.AddHours(1)), waiting.NextDue);
        Assert.Equal(Option<SourcedTask>.Some(task), offered.Task);
        Assert.Empty(empty.Marks);
        Assert.Equal([TaskState.Taken], later.Marks.Select(mark => mark.Mark.State));
        Assert.Equal(AutopilotError.Malformed, Outcomes.FailsWith(await sources.NextAsync(Request(Now), Cancellation)));
    }

    private static async Task<SourcedTask> NextTaskAsync(IJobSource source, DateTimeOffset? now = null) =>
        Outcomes.Present(Outcomes.Succeeds(await source.NextAsync(Request(now ?? Now), Cancellation)).Task);

    private static SourceRequest Request(DateTimeOffset now) => new(LoopId.New(), Repository, now);

    private static string Backlog(params (string Id, string Instruction)[] tasks) =>
        $$"""{ "tasks": [ {{string.Join(", ", tasks.Select(task => $$"""{ "id": "{{task.Id}}", "instruction": "{{task.Instruction}}" }"""))}} ] }""";
}
