using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Triggers.Contracts;
using Avala.Workbench.Triggers;

namespace Avala.Workbench.Tests.Triggers;

public sealed class ForgePhrasesTests
{
    private static readonly TriggerId Id = new(TriggerId.Machine, "fix");

    private static readonly DateTimeOffset At = new(2026, 10, 9, 13, 0, 0, TimeSpan.Zero);

    private static ForgeEventWatch Watching => new(Id, new ForgeName("work"), ForgeEventKind.IssueOpened) { Forge = "Forgejo", Status = EventWatchStatus.Watching, NextPoll = At.AddMinutes(5) };

    [Fact]
    public void AForgeTriggerSaysItsForgeAndTheEventThatFiresItOrItsConnectionBeforeItsForgeIsKnown()
    {
        var rule = new ForgeRule("work", ForgeEventKind.IssueOpened, ["alice"]);

        Assert.Equal(
            [
                "Forgejo · issue opened",
                "work · issue labeled \"avala\"",
                "Forgejo · comment \"/avala fix\"",
                "Forgejo · checks failed on \"main\"",
                "Forgejo · review requested from \"avala-bot\"",
                "Forgejo · review requested",
            ],
            [
                ForgePhrases.Fires(rule, Watching),
                ForgePhrases.Fires(rule with { Event = ForgeEventKind.IssueLabeled, Label = "avala" }, Option<ForgeEventWatch>.None),
                ForgePhrases.Fires(rule with { Event = ForgeEventKind.CommentCommand, Command = "/avala fix" }, Watching),
                ForgePhrases.Fires(rule with { Event = ForgeEventKind.ChecksFailed, Branch = "main" }, Watching),
                ForgePhrases.Fires(rule with { Event = ForgeEventKind.ReviewRequested, Reviewer = "avala-bot" }, Watching),
                ForgePhrases.Fires(rule with { Event = ForgeEventKind.ReviewRequested }, Watching),
            ]);
    }

    [Fact]
    public void ThePollSaysWhenItIsNextOrWhyItIsRetrying() =>
        Assert.Equal(
            ["Next poll · 13:05", "Retrying after Unauthorized · 2 failures · next poll 13:05", "Waiting for its first poll", "Not polled: no forge reads its events"],
            [
                ForgePhrases.Next(Watching, TimeZoneInfo.Utc),
                ForgePhrases.Next(Watching with { Status = EventWatchStatus.BackingOff, Failure = ForgeError.Unauthorized, Failures = 2 }, TimeZoneInfo.Utc),
                ForgePhrases.Next(Watching with { Status = EventWatchStatus.Waiting }, TimeZoneInfo.Utc),
                ForgePhrases.Next(Option<ForgeEventWatch>.None, TimeZoneInfo.Utc),
            ]);

    [Fact]
    public void AnEventSaysWhatHappenedWhoDidItAndItsVerdict()
    {
        var entry = new ForgeEventEntry(Id, At, ForgeEventKind.CommentCommand, "9", "alice", ForgeEventVerdict.Duplicate) { Number = 12, Detail = "/avala fix" };

        Assert.Equal(
            [
                "Fri 9 Oct 13:00 · fix · comment on #12 \"/avala fix\" by alice · duplicate, already fired",
                "Fri 9 Oct 13:00 · fix · checks failed on main by alice · filtered: a fork's pull request",
                "Fri 9 Oct 13:00 · fix · issue #12 opened by someone unknown · fired, skipped at its concurrency cap",
                "Fri 9 Oct 13:00 · fix · review of #12 requested from carol by alice · not allowed: not on the authors list",
            ],
            [
                ForgePhrases.Entry(entry, TimeZoneInfo.Utc),
                ForgePhrases.Entry(entry with { Kind = ForgeEventKind.ChecksFailed, Detail = "main", Verdict = ForgeEventVerdict.Filtered, Reason = FilterReason.FromFork }, TimeZoneInfo.Utc),
                ForgePhrases.Entry(entry with { Kind = ForgeEventKind.IssueOpened, Actor = string.Empty, Verdict = ForgeEventVerdict.Fired, Outcome = RunOutcome.AtConcurrencyCap }, TimeZoneInfo.Utc),
                ForgePhrases.Entry(entry with { Kind = ForgeEventKind.ReviewRequested, Detail = "carol", Verdict = ForgeEventVerdict.NotAllowed }, TimeZoneInfo.Utc),
            ]);
    }

    [Fact]
    public void ARunFiredByAForgeEventNamesTheForgeAndActorAndADuplicateSaysNothingStarted() =>
        Assert.Equal(
            "Fri 9 Oct 13:00 · forge work · alice · already fired for this event",
            TriggerPhrases.Run(new TriggerRun(Guid.NewGuid(), Id, TriggerOrigin.External, "forge work · alice", At, RunOutcome.Duplicate), TimeZoneInfo.Utc));
}
