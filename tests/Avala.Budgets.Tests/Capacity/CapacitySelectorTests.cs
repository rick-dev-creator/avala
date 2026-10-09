using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.BudgetFiles;
using Avala.Budgets.Routing;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Budgets.Tests.Capacity;

public sealed class CapacitySelectorTests
{
    private const string Worktree = "/worktrees/1";

    private static readonly ConnectionName Work = new("work");
    private static readonly ConnectionName Personal = new("personal");
    private static readonly ConnectionName Spare = new("spare");

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero));
    private readonly Readings readings = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheCandidateWithTheMostRemainingCapacityIsChosenAndOneWithoutReadingsCountsAsUnusedAsync()
    {
        readings.Of[Work] = [Window(0.6), Window(0.2, "weekly")];
        readings.Of[Spare] = [Window(0.1)];

        var choice = await ChooseAsync(Work, Personal, Spare);

        Assert.Equal((Personal, ChoiceReason.MostCapacity, clock.GetUtcNow()), (choice.Connection, choice.Reason, choice.At));
        Assert.Equal(
            [
                new CandidateCapacity(Work, 0.6, Window(0.6), 1, true),
                new CandidateCapacity(Personal, 0, Option<UsageLimit>.None, 1, true),
                new CandidateCapacity(Spare, 0.1, Window(0.1), 1, true),
            ],
            choice.Compared);
    }

    [Fact]
    public async Task ATieGoesToTheCandidateDeclaredFirstAsync()
    {
        readings.Of[Work] = [Window(0.3)];
        readings.Of[Personal] = [Window(0.3)];

        Assert.Equal(Personal, (await ChooseAsync(Personal, Work)).Connection);
        Assert.Equal(Work, (await ChooseAsync(Work, Personal)).Connection);
    }

    [Fact]
    public async Task ACandidateAtTheHoldThresholdOfItsConnectionIsSkippedAsync()
    {
        readings.Of[Work] = [Window(0.6)];
        readings.Of[Personal] = [Window(0.5)];
        var files = new CommittedFiles().With(Worktree, ".avala/budget.json", """{ "holdAtLimit": 0.9, "connections": { "personal": { "holdAtLimit": 0.5 } } }""");

        var choice = await ChooseAsync(files, Personal, Work);

        Assert.Equal((Work, ChoiceReason.MostCapacity), (choice.Connection, choice.Reason));
        Assert.Equal([(Personal, 0.5, false), (Work, 0.9, true)], choice.Compared.Select(candidate => (candidate.Connection, candidate.Threshold, candidate.Available)));
    }

    [Fact]
    public async Task AWindowThatHasResetNoLongerCountsAndOneWithoutAResetTimeAlwaysDoesAsync()
    {
        readings.Of[Work] = [new UsageLimit("5h", 0.95, clock.GetUtcNow().AddMinutes(-1))];
        readings.Of[Personal] = [new UsageLimit("weekly", 0.4, Option<DateTimeOffset>.None)];

        var choice = await ChooseAsync(Personal, Work);

        Assert.Equal(Work, choice.Connection);
        Assert.Equal([0.4, 0], choice.Compared.Select(candidate => candidate.Used));
    }

    [Fact]
    public async Task WhenEveryCandidateIsAtItsLimitTheOneWithTheMostRemainingIsChosenAsAllAtLimitAsync()
    {
        readings.Of[Work] = [Window(0.95)];
        readings.Of[Personal] = [Window(0.92)];
        var files = new CommittedFiles().With(Worktree, ".avala/budget.json", """{ "holdAtLimit": 0.9 }""");

        var choice = await ChooseAsync(files, Work, Personal);

        Assert.Equal((Personal, ChoiceReason.AllAtLimit), (choice.Connection, choice.Reason));
        Assert.All(choice.Compared, candidate => Assert.False(candidate.Available));
    }

    [Fact]
    public async Task WithoutADeclaredThresholdOnlyASpentWindowIsSkippedAsync()
    {
        readings.Of[Work] = [Window(1)];
        readings.Of[Personal] = [Window(0.99)];

        var choice = await ChooseAsync(Work, Personal);

        Assert.Equal((Personal, ChoiceReason.MostCapacity), (choice.Connection, choice.Reason));
        Assert.Equal([false, true], choice.Compared.Select(candidate => candidate.Available));
    }

    [Fact]
    public async Task AQuestionAboutARepositoryReadsTheThresholdsOfItsCurrentCommitAsync()
    {
        readings.Of[Work] = [Window(0.6)];
        readings.Of[Personal] = [Window(0.7)];
        var selector = new CapacitySelector(new BudgetFileReader(new HeadOnlyFiles("""{ "holdAtLimit": 0.65 }""")), readings, clock);

        var choice = Outcomes.Present(await selector.ChooseAsync(new ConnectionQuestion("/repositories/shop", [Personal, Work]) { AtHead = true }, Cancellation));

        Assert.Equal((Work, ChoiceReason.MostCapacity), (choice.Connection, choice.Reason));
        Assert.Equal([(Personal, 0.65, false), (Work, 0.65, true)], choice.Compared.Select(candidate => (candidate.Connection, candidate.Threshold, candidate.Available)));
    }

    private Task<ConnectionChoice> ChooseAsync(params ConnectionName[] candidates) =>
        ChooseAsync(new CommittedFiles().Workspace(Worktree), candidates);

    private async Task<ConnectionChoice> ChooseAsync(CommittedFiles files, params ConnectionName[] candidates) =>
        Outcomes.Present(await new CapacitySelector(new BudgetFileReader(files), readings, clock)
            .ChooseAsync(new ConnectionQuestion(Worktree, candidates), Cancellation));

    private UsageLimit Window(double used, string window = "5h") => new(window, used, clock.GetUtcNow().AddHours(2));

    private sealed class Readings : IUsage
    {
        private static readonly ProviderInfo Provider = new("simulator", "Simulator");

        public Dictionary<ConnectionName, IReadOnlyList<UsageLimit>> Of { get; } = [];

        public IReadOnlyList<ConnectionUsage> ByConnection() =>
            [.. Of.Select(pair => new ConnectionUsage(pair.Key, Provider, new UsageSummary(default, [], 0, default, pair.Value)))];

        public IReadOnlyList<ProviderUsage> ByProvider() => [];

        public IReadOnlyList<AccountUsage> ByAccount() => [];

        public Option<UsageSummary> OfSession(SessionId session) => Option<UsageSummary>.None;

        public Option<UsageSummary> OfJob(JobId job) => Option<UsageSummary>.None;
    }

    private sealed class HeadOnlyFiles(string budget) : IBaseFiles
    {
        public ValueTask<Result<BaseFile, WorkspaceFailure>> ReadAsync(string worktree, string path, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<BaseFile, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace));

        public ValueTask<Result<BaseFile, WorkspaceFailure>> ReadCurrentAsync(string repository, string path, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<BaseFile, WorkspaceFailure>.Success(new BaseFile(path, CommittedFiles.Origin(), budget)));
    }
}
