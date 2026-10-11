using Avala.Agents.Contracts.Events;
using Avala.Budgets.Admission;
using Avala.Budgets.BudgetFiles;
using Avala.Budgets.Contracts;
using Avala.Budgets.Tests.Enforcement;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Budgets.Tests.Admission;

public sealed class RunningJobsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobBeyondTheLimitWaitsUntilARunningJobLeavesItsSlotAsync()
    {
        var bus = new RecordingBus();
        await using var admission = Limited(bus, 1);
        var first = JobId.New();
        var second = JobId.New();

        await admission.AdmitAsync(first, Cancellation);
        await admission.HandleAsync(new JobProgressed(first, JobStatus.Running), Cancellation);
        var waiting = admission.AdmitAsync(second, Cancellation).AsTask();
        Assert.Equal(new JobQueued(second, 1, 1), await bus.WaitForAsync<JobQueued>(_ => true, Cancellation));
        await admission.HandleAsync(new JobProgressed(first, JobStatus.Checking), Cancellation);
        Assert.False(waiting.IsCompleted);

        await admission.HandleAsync(new JobProgressed(first, JobStatus.AwaitingReview), Cancellation);
        await waiting;

        Assert.Equal(new JobAdmitted(second), bus.Published[^1]);
    }

    [Fact]
    public async Task AWaitingJobThatEndsIsLetThroughWithoutTakingASlotAsync()
    {
        var bus = new RecordingBus();
        await using var admission = Limited(bus, 1);
        var running = JobId.New();
        var discarded = JobId.New();
        var next = JobId.New();
        await admission.AdmitAsync(running, Cancellation);
        var waiting = admission.AdmitAsync(discarded, Cancellation).AsTask();
        var queued = admission.AdmitAsync(next, Cancellation).AsTask();
        await bus.WaitForAsync<JobQueued>(queued => queued.Job == next, Cancellation);

        await admission.HandleAsync(new JobProgressed(discarded, JobStatus.Discarded), Cancellation);
        await waiting;
        Assert.False(queued.IsCompleted);
        await admission.HandleAsync(new JobProgressed(running, JobStatus.Failed), Cancellation);

        await queued;
    }

    [Fact]
    public async Task WithoutALimitEveryJobIsAdmittedAtOnceAsync()
    {
        var bus = new RecordingBus();
        await using var admission = new RunningJobs(new Budgeted.FixedMachine(), bus);

        await admission.AdmitAsync(JobId.New(), Cancellation);
        await admission.AdmitAsync(JobId.New(), Cancellation);

        Assert.Empty(bus.Published);
    }

    [Theory]
    [InlineData("""{ "runningJobs": 2 }""", "2")]
    [InlineData("{}", "none")]
    [InlineData("""{ "runningJobs": 0 }""", "InvalidRunningJobs")]
    [InlineData("""{ "runningJobs": 1.5 }""", "InvalidRunningJobs")]
    [InlineData("""{ "runningJobs": "2" }""", "Malformed")]
    [InlineData("""{ "parallel": 2 }""", "UnknownField")]
    [InlineData("[]", "Malformed")]
    [InlineData("""{ "spendPerDay": { "USD": 40, "EUR": 5.5 } }""", "none · 40 USD + 5.5 EUR")]
    [InlineData("""{ "runningJobs": 2, "connections": { "team-api": { "spendPerDay": { "USD": 10 } } } }""", "2 · team-api 10 USD")]
    [InlineData("""{ "spendPerDay": { "USD": 0 } }""", "InvalidCost")]
    [InlineData("""{ "spendPerDay": { "": 5 } }""", "InvalidCost")]
    [InlineData("""{ "spendPerDay": { "USD": "5" } }""", "Malformed")]
    [InlineData("""{ "spendPerDay": 5 }""", "Malformed")]
    [InlineData("""{ "connections": { " ": { "spendPerDay": { "USD": 10 } } } }""", "Malformed")]
    [InlineData("""{ "connections": { "team-api": { "runningJobs": 2 } } }""", "UnknownField")]
    [InlineData("""{ "connections": [] }""", "Malformed")]
    public void TheMachineBudgetIsParsedStrictly(string text, string expected) =>
        Assert.Equal(
            expected,
            MachineBudgetFile.Parse(text).Match(
                parsed => string.Join(
                    " · ",
                    [
                        parsed.RunningJobs.Match(most => most.ToString(System.Globalization.CultureInfo.InvariantCulture), () => "none"),
                        .. parsed.SpendPerDay.Count > 0 ? [Costs(parsed.SpendPerDay)] : Array.Empty<string>(),
                        .. parsed.Connections.Select(ceiling => $"{ceiling.Connection.Value} {Costs(ceiling.SpendPerDay)}"),
                    ]),
                failure => failure.ToString()));

    private static string Costs(IReadOnlyList<Cost> costs) =>
        string.Join(" + ", costs.Select(cost => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{cost.Amount} {cost.Currency}")));

    [Theory]
    [InlineData(null, BudgetFileStatus.Absent, null)]
    [InlineData("""{ "runningJobs": 3 }""", BudgetFileStatus.Applied, 3)]
    [InlineData("""{ "runningJobs": -1 }""", BudgetFileStatus.Rejected, 1)]
    public async Task ARejectedMachineBudgetRunsOneJobAtATimeAsync(string? text, BudgetFileStatus status, int? limit)
    {
        using var data = new TemporaryFolder();

        if (text is not null)
        {
            await File.WriteAllTextAsync(Path.Combine(data.Path, MachineBudgetFile.FileName), text, Cancellation);
        }

        var budget = await new MachineBudgetFile(new AvalaPaths(data.Path)).LoadAsync(Cancellation);

        Assert.Equal((status, limit), (budget.File, budget.RunningJobs.Match<int?>(most => most, () => null)));
    }

    private static RunningJobs Limited(RecordingBus bus, int limit) =>
        new(new Budgeted.FixedMachine { Budget = new MachineBudget(limit, BudgetFileStatus.Applied, Option<BudgetError>.None) }, bus);
}
