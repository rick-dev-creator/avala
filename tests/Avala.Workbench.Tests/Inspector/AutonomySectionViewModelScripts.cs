using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Workbench.Presenting;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Inspector;

namespace Avala.Workbench.Tests.Inspector;

public sealed class AutonomySectionViewModelScripts : IDisposable
{
    private readonly Bench bench = new();

    [Fact]
    public async Task AJobInFocusShowsHowItsAutonomyWasTightenedAndItsConnection()
    {
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);

        Assert.Equal(("Supervised, tightened from the repository's Autonomous", "claude-personal"), (section.Autonomy, section.Connection));
    }

    [Fact]
    public async Task AJobPlacedByCapacitySaysWhyWithTheReadingsItComparedAsync()
    {
        var job = bench.Job("Fix the flaky checkout test", JobStatus.Running);
        var choice = Outcomes.Present(FakePreview.ByCapacity("claude-personal", ChoiceReason.MostCapacity, ("claude-work", 0.95, false), ("claude-personal", 0.31, true)).Choice);
        bench.Publish(Bench.OnBoard(job) with { Choice = choice });
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal("Chosen by capacity: claude-personal had the most left", section.Reason);
        Assert.Equal(
            [
                new CapacityLine("claude-work", "95% of 5h · holds at 90% · at its limit", false, true),
                new CapacityLine("claude-personal", "31% of 5h · holds at 90%", true, false),
            ],
            section.Compared);
    }

    [Fact]
    public async Task AJobPlacedWhileEveryConnectionWasAtItsLimitSaysSoAsync()
    {
        var job = bench.Job("Fix the flaky checkout test", JobStatus.Running);
        var choice = Outcomes.Present(FakePreview.ByCapacity("claude-personal", ChoiceReason.AllAtLimit, ("claude-work", 0.95, false), ("claude-personal", 0.92, false)).Choice);
        bench.Publish(Bench.OnBoard(job) with { Choice = choice });
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal("Chosen by capacity: every connection was at its limit, so claude-personal, the least used", section.Reason);
    }

    [Fact]
    public async Task AHandedOffJobListsEachHandoffAndWhatItSpentOnEachConnectionAsync()
    {
        var job = bench.Job("Fix the flaky checkout test", JobStatus.Running);
        var at = new DateTimeOffset(2026, 10, 10, 10, 42, 0, TimeSpan.Zero);
        var handoff = new HandoffRecord(job.Job, 2, Work, Personal, new LimitReason(Work, "5h", 0.91, 0.9), at) { Spent = [new Cost(1.84m, "USD")], Tokens = 412_880 };
        bench.Usage.Jobs[job.Job] = new UsageSummary(new TokenUsage(400_000, 50_000, 20_000, 4_080, 0), [new Cost(2.15m, "USD")], 0, default, []);
        bench.Publish(Bench.OnBoard(job) with { Handoffs = [handoff] });
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal(
            [
                new HandoffLine($"Handed off from claude-work to claude-personal at 91% of the 5-hour window · {Amounts.Time(at)}", "Spent on claude-work: 1.84 USD · 412,880 tokens"),
                new HandoffLine(string.Empty, "Spent on claude-personal: 0.31 USD · 61,200 tokens"),
            ],
            section.Handoffs);
        Assert.Equal(string.Empty, section.Waiting);
    }

    [Fact]
    public async Task AHandoffThatFellBackToTheDestinationsDefaultModelSaysSoAsync()
    {
        var job = bench.Job("Fix the flaky checkout test", JobStatus.Running);
        var at = new DateTimeOffset(2026, 10, 10, 10, 42, 0, TimeSpan.Zero);
        var handoff = new HandoffRecord(job.Job, 2, Work, Personal, new LimitReason(Work, "5h", 0.91, 0.9), at)
        {
            Model = new ModelFallback(new ModelChoice("claude-opus-5-5", Option<string>.None), new ModelChoice("gpt-codex", Option<string>.None)),
        };
        bench.Publish(Bench.OnBoard(job) with { Handoffs = [handoff] });
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal(
            $"Handed off from claude-work to claude-personal at 91% of the 5-hour window · model claude-opus-5-5 not offered by claude-personal, ran with its default gpt-codex · {Amounts.Time(at)}",
            section.Handoffs[0].Moved);
    }

    [Fact]
    public async Task AJobWaitingForAResetSaysWhenItResumesAsync()
    {
        var job = bench.Job("Fix the flaky checkout test", JobStatus.NeedsHelp);
        var since = new DateTimeOffset(2026, 10, 10, 0, 20, 0, TimeSpan.Zero);
        var resumes = since.AddHours(2).AddMinutes(50);
        bench.Publish(Bench.OnBoard(job) with { Wait = new ResetWait(job.Job, Work, "5h", resumes, since) });
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal($"Resumes at {resumes.ToLocalTime():HH:mm} when the 5-hour window resets", section.Waiting);
        Assert.Empty(section.Handoffs);
    }

    [Fact]
    public async Task AJobWaitingForAWindowThatReportsNoResetSaysItWaitsForYouAsync()
    {
        var job = bench.Job("Fix the flaky checkout test", JobStatus.NeedsHelp);
        bench.Publish(Bench.OnBoard(job) with { Wait = new ResetWait(job.Job, Work, "5h", Option<DateTimeOffset>.None, DateTimeOffset.UnixEpoch) });
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal("Near its limit · no reset time reported, waits for you", section.Waiting);
    }

    [Fact]
    public async Task AJobThatHasNotStartedSaysSoOnTheDefaultConnection()
    {
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(bench.Job("Add invoice PDF endpoint", JobStatus.Preparing).Job, bench);

        Assert.Equal(("Not started yet", "The default connection", string.Empty, 0), (section.Autonomy, section.Connection, section.Reason, section.Compared.Count));
    }

    [Fact]
    public async Task AJobThatAskedForSupervisionBeforeStartingSaysSo()
    {
        var job = bench.Job("Update lodash to 4.17.21", JobStatus.Preparing);
        bench.Catalog.Change(job.Job, history => history with { Summary = history.Summary with { Autonomy = Autonomy.Supervised } });
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal("Supervised, as asked", section.Autonomy);
    }

    [Fact]
    public async Task WithNothingInFocusTheSectionIsEmpty()
    {
        using var section = new AutonomySectionViewModel(bench.Inspected());

        await section.FocusAsync(Option<JobId>.None, bench);

        Assert.Equal((string.Empty, string.Empty), (section.Autonomy, section.Connection));
    }

    [Fact]
    public async Task TheHeaderFactIsTheAutonomyTheJobRunsUnder()
    {
        using var section = new AutonomySectionViewModel(bench.Inspected());
        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);
        var reviewed = section.Fact;

        await section.FocusAsync(bench.Job("Fix JPY rounding in invoice totals", JobStatus.Preparing).Job, bench);

        Assert.Equal(("Supervised", "not started"), (reviewed, section.Fact));
    }

    private static ConnectionName Work => new("claude-work");

    private static ConnectionName Personal => new("claude-personal");

    public void Dispose() => bench.Dispose();
}
