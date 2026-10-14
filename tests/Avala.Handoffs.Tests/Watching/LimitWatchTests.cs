using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Handoffs.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Handoffs.Tests.Watching;

public sealed class LimitWatchTests
{
    private const string Feedback = "Two tests fail.";

    private static readonly LimitRules SameHarness = new(OnLimit.HandOffSameHarness, LimitRules.DefaultThreshold, [Watched.Work, Watched.Personal]);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AJobHeldAtItsLimitIsHandedToTheConnectionWithRoomWithABriefOfItsWorkAsync()
    {
        await using var watched = new Watched();
        watched.Rules.Rules = SameHarness;
        watched.Reading(Watched.Work, 0.95, Watched.Start.AddHours(2));
        watched.Reading(Watched.Personal, 0.2, Option<DateTimeOffset>.None);
        watched.Watch.Pending(watched.Job, Feedback);

        await watched.HeldAsync(HoldReason.LimitNearlyReached);
        var moved = await watched.MovedAsync();

        Assert.Equal("hand off", moved.Operation);
        var choice = Outcomes.Present(moved.Handoff).Choice;
        Assert.Equal((Watched.Personal, ChoiceReason.MostCapacity), (choice.Connection, choice.Reason));
        Assert.Contains(Watched.Instruction, moved.Text, StringComparison.Ordinal);
        Assert.Contains(Feedback, moved.Text, StringComparison.Ordinal);
        Assert.Empty(watched.Store.Waits);
    }

    [Fact]
    public async Task AJobWhoseConnectionIsNoLongerDeclaredMovesWithinTheHarnessItsUsageNamesAsync()
    {
        await using var watched = new Watched { Running = new ConnectionName("retired") };
        watched.Status(JobStatus.NeedsHelp);
        watched.Rules.Rules = SameHarness with { Connections = [new ConnectionName("retired"), Watched.Personal] };
        watched.Reading(new ConnectionName("retired"), 0.95, Watched.Start.AddHours(2));
        watched.Reading(Watched.Personal, 0.2, Option<DateTimeOffset>.None);

        await watched.HeldAsync(HoldReason.LimitNearlyReached);

        Assert.Equal(Watched.Personal, Outcomes.Present((await watched.MovedAsync()).Handoff).Choice.Connection);
    }

    [Fact]
    public async Task WithNowhereToGoAHeldJobWaitsForItsWindowAndIsContinuedWithItsFeedbackOnceItResetsWhenItsRulesResumeByThemselvesAsync()
    {
        await using var watched = new Watched();
        watched.Rules.Rules = LimitRules.Default with { Resume = ResumeAtReset.Auto };
        var resets = Watched.Start.AddHours(1);
        watched.Reading(Watched.Work, 0.95, resets);
        watched.Watch.Pending(watched.Job, Feedback);

        await watched.HeldAsync(HoldReason.LimitNearlyReached);
        var wait = await watched.WaitingAsync();
        Assert.Equal(new ResetWait(watched.Job, Watched.Work, "5h", resets, Watched.Start), wait);
        Assert.Equal(Option<ResetWait>.Some(wait), watched.Book.WaitOf(watched.Job));
        Assert.Equal([new KeptWait(watched.Job, Feedback, Watched.Start)], watched.Store.Waits);

        watched.Clock.Advance(TimeSpan.FromHours(1));
        var moved = await watched.MovedAsync();
        var resumed = await watched.Bus.WaitForAsync<JobResumedAtReset>(found => found.Job == watched.Job, Cancellation);
        await watched.DroppedAsync();

        Assert.Equal(("continue", $"{LimitActions.ResetMessage}\n\n{Feedback}"), (moved.Operation, moved.Text));
        Assert.Equal(new JobResumedAtReset(watched.Job, Watched.Work, ContinuedIn.ResumedConversation, resets), resumed);
        Assert.Equal(Option<ResetWait>.None, watched.Book.WaitOf(watched.Job));
    }

    [Fact]
    public async Task AJobHeldBecauseItsWindowRanOutWaitsForThatWindowsResetBeforeAnyReadingOfItArrivesAsync()
    {
        await using var watched = new Watched();
        var resets = Watched.Start.AddHours(1);
        watched.Reading(Watched.Work, 0.3, Option<DateTimeOffset>.None);

        await watched.HeldAsync(HoldReason.LimitNearlyReached, new UsageLimit("5h", 1, resets));

        Assert.Equal(new ResetWait(watched.Job, Watched.Work, "5h", resets, Watched.Start) { AwaitsPerson = true }, await watched.WaitingAsync());
    }

    [Fact]
    public async Task ByDefaultAHeldJobIsNotContinuedAtTheResetAndItsWaitSaysTheWindowResetForThePersonAsync()
    {
        await using var watched = new Watched();
        var resets = Watched.Start.AddHours(1);
        watched.Reading(Watched.Work, 0.95, resets);
        await watched.HeldAsync(HoldReason.LimitNearlyReached);
        var before = await watched.WaitingAsync();

        watched.Clock.Advance(TimeSpan.FromHours(1));
        var after = (await watched.Bus.WaitForAsync<JobWaitsForReset>(found => found.Wait.Job == watched.Job && found.Wait.HasReset, Cancellation)).Wait;

        Assert.Equal((true, false), (before.AwaitsPerson, before.HasReset));
        Assert.Equal(before with { HasReset = true }, after);
        Assert.Equal(Option<ResetWait>.Some(after), watched.Book.WaitOf(watched.Job));
        Assert.Empty(watched.Jobs.Calls.Entries);
        Assert.DoesNotContain(watched.Bus.Published, published => published is JobResumedAtReset);
    }

    [Fact]
    public async Task AJobThatWaitsForThePersonStopsWaitingOnceTheyResumeItAsync()
    {
        await using var watched = new Watched();
        watched.Reading(Watched.Work, 0.95, Watched.Start.AddHours(1));
        await watched.HeldAsync(HoldReason.LimitNearlyReached);
        _ = await watched.WaitingAsync();
        watched.Clock.Advance(TimeSpan.FromHours(1));
        _ = await watched.Bus.WaitForAsync<JobWaitsForReset>(found => found.Wait.HasReset, Cancellation);

        await watched.ProgressedAsync(JobStatus.Running);
        await watched.DroppedAsync();

        Assert.Equal(Option<ResetWait>.None, watched.Book.WaitOf(watched.Job));
    }

    [Fact]
    public async Task AHandoffTheJobsRefuseLeavesTheJobWaitingForTheEarliestResetAsync()
    {
        await using var watched = new Watched();
        watched.Rules.Rules = SameHarness;
        watched.Jobs.Refusal = JobRejection.NotHeld;
        watched.Reading(Watched.Work, 0.95, Watched.Start.AddHours(2));
        watched.Reading(Watched.Personal, 0.2, Option<DateTimeOffset>.None);

        await watched.HeldAsync(HoldReason.LimitNearlyReached);

        Assert.Equal("hand off", (await watched.MovedAsync()).Operation);
        Assert.Equal(Option<DateTimeOffset>.Some(Watched.Start.AddHours(2)), (await watched.WaitingAsync()).ResumesAt);
    }

    [Fact]
    public async Task AWaitingJobThatMovesOnByOtherMeansStopsWaitingAsync()
    {
        await using var watched = new Watched();
        watched.Reading(Watched.Work, 0.95, Watched.Start.AddHours(1));
        await watched.HeldAsync(HoldReason.LimitNearlyReached);
        _ = await watched.WaitingAsync();

        await watched.ProgressedAsync(JobStatus.Running);
        await watched.DroppedAsync();

        Assert.Empty(watched.Store.Waits);
        Assert.Equal(Option<ResetWait>.None, watched.Book.WaitOf(watched.Job));
    }

    [Fact]
    public async Task AWaitKeptBeforeARestartContinuesTheJobAtStartupWhenItsWindowHasResetAndItsRulesResumeByThemselvesAsync()
    {
        await using var watched = new Watched();
        watched.Rules.Rules = LimitRules.Default with { Resume = ResumeAtReset.Auto };
        await watched.Store.KeepAsync(new KeptWait(watched.Job, Feedback, Watched.Start.AddHours(-2)), Cancellation);
        watched.Reading(Watched.Work, 0.95, Watched.Start.AddMinutes(-1));

        await watched.Watch.RunAsync(Cancellation);
        var moved = await watched.MovedAsync();
        await watched.DroppedAsync();

        Assert.Equal(("continue", $"{LimitActions.ResetMessage}\n\n{Feedback}"), (moved.Operation, moved.Text));
    }

    [Fact]
    public async Task AWaitKeptForAJobThatNoLongerNeedsHelpIsDroppedAtStartupAsync()
    {
        await using var watched = new Watched();
        watched.Status(JobStatus.Approved);
        await watched.Store.KeepAsync(new KeptWait(watched.Job, Option<string>.None, Watched.Start.AddHours(-2)), Cancellation);

        await watched.Watch.RunAsync(Cancellation);
        await watched.DroppedAsync();

        Assert.Empty(watched.Jobs.Calls.Entries);
        Assert.Empty(watched.Store.Waits);
    }
}
