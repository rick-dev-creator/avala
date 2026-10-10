using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Tests.Inspector;

public sealed class ForgePhrasesTests
{
    private static readonly PullRequestWatchState Watch =
        new(new JobId(Guid.CreateVersion7()), new ForgeName("github"), new PullRequestRef(7, new Uri("https://github.com/octo/shop/pull/7"), "avala/fix", "main"), OnPullRequest.WakeOnCi, 3);

    private static readonly DateTimeOffset HalfPastTwo = new(2026, 3, 2, 14, 30, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 3, 2, 14, 30, 0, DateTimeKind.Local)));

    public static TheoryData<PullRequestWatchState, string> Statuses => new()
    {
        { Watch, "Watching" },
        { Watch with { NextPoll = HalfPastTwo }, "Watching · next check 14:30" },
        { Watch with { Status = WatchStatus.BackingOff, Failure = ForgeError.Unreachable, Failures = 1 }, "Retrying after Unreachable · 1 failure" },
        { Watch with { Status = WatchStatus.BackingOff, Failures = 2, NextPoll = HalfPastTwo }, "Retrying after an error · 2 failures · next check 14:30" },
        { Watch with { Status = WatchStatus.WaitingForJob }, "Waiting for the job" },
        { Watch with { Status = WatchStatus.WaitingForJob, WakeUps = 2 }, "The agent works on wake-up 2 of 3" },
        { Watch with { Status = WatchStatus.NeedsPerson, Pending = WakeReason.Conflict }, "Held for a person: conflicts with its base" },
        { Watch with { Status = WatchStatus.NeedsPerson }, "Held for a person: the wake-ups are spent" },
        { Watch with { Status = WatchStatus.Ended, Ended = WatchEnd.Green }, "Watch ended: checks passed" },
        { Watch with { Status = WatchStatus.Ended }, "Watch ended: ended" },
    };

    [Theory]
    [InlineData(ForgeError.UnknownConnection, "the repository names a forge that forges.json does not declare.")]
    [InlineData(ForgeError.UnknownForge, "no plugin of that forge is installed.")]
    [InlineData(ForgeError.MissingUrl, "the forge connection needs a url.")]
    [InlineData(ForgeError.MissingCredential, "the token's environment variable is not set.")]
    [InlineData(ForgeError.CliUnavailable, "the forge's command-line tool is not installed.")]
    [InlineData(ForgeError.Unauthorized, "the forge refused the credential.")]
    [InlineData(ForgeError.NotFound, "the forge does not know the repository or the pull request.")]
    [InlineData(ForgeError.RateLimited, "the forge's rate limit is spent for now.")]
    [InlineData(ForgeError.Rejected, "the forge rejected the request.")]
    [InlineData(ForgeError.Unreachable, "the forge could not be reached.")]
    [InlineData(ForgeError.InvalidRemote, "the remote's url names no owner and repository.")]
    [InlineData(ForgeError.NoBaseBranch, "the job's base is not a branch.")]
    [InlineData(ForgeError.PushRejected, "the remote refused the push of the job's branch.")]
    [InlineData(ForgeError.GitFailed, "git could not read the remote.")]
    [InlineData(ForgeError.MissingForge, "the repository's .avala/jobs.json has no pullRequest section naming a forge.")]
    [InlineData(ForgeError.InvalidInterval, "forges.json or the pullRequest section is invalid (InvalidInterval).")]
    public void AForgeErrorSaysWhatWentWrong(ForgeError error, string phrase) =>
        Assert.Equal(phrase, ForgePhrases.Error(error));

    [Theory]
    [MemberData(nameof(Statuses))]
    public void AWatchStatusSaysWhatTheWatchDoesAndWhenItChecksNext(PullRequestWatchState state, string phrase) =>
        Assert.Equal(phrase, ForgePhrases.Status(state));
}
