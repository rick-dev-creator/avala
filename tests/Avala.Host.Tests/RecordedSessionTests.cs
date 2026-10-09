using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Testing;
using Avala.Verification.Contracts;

namespace Avala.Host.Tests;

public sealed class RecordedSessionTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Fixtures => [.. RecordingFixtures.Names];

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task ACommittedRecordingReplaysToItsExpectedOutcomeAsync(string name)
    {
        var fixture = await RegressionFixture.LoadAsync(name, Cancellation);

        var replayed = await ReplayAsync(name, await File.ReadAllTextAsync(RecordingFixtures.RecordingOf(name), Cancellation), fixture);

        Assert.Equal(fixture.Expected, replayed);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task RecordingASessionAndReplayingTheRecordingReachTheSameOutcomeAsync(string name)
    {
        var fixture = await RegressionFixture.LoadAsync(name, Cancellation);
        Assert.SkipWhen(fixture.Record is null, $"{name} was recorded from a provider the tests cannot run.");
        await using var run = await SimulatedRun.InstructedAsync(plugins, fixture.Record, [("recording.json", """{ "enabled": true }""")], fixture.Committed);

        var recorded = await OutcomeAsync(run, fixture);
        var recording = Assert.Single(await run.StopAndReadRecordingsAsync());
        var replayed = await ReplayAsync(name, recording, fixture);

        Assert.Equal(fixture.Expected, recorded);
        Assert.Equal(recorded, replayed);

        if (RecordingFixtures.Refreshing)
        {
            await File.WriteAllTextAsync(RecordingFixtures.RecordingOf(name), recording, Cancellation);
        }
    }

    private async Task<Outcome> ReplayAsync(string name, string recording, RegressionFixture fixture)
    {
        await using var run = await SimulatedRun.InstructedAsync(
            plugins,
            RecordingFixtures.Replay(name),
            [($"recordings/{name}.json", recording)],
            fixture.Committed);

        return await OutcomeAsync(run, fixture);
    }

    private static async Task<Outcome> OutcomeAsync(SimulatedRun run, RegressionFixture fixture)
    {
        var journey = await run.JourneyAsync();
        var audit = run.Get<IPermissionAudit>();

        return new Outcome(
            [.. journey.SkipWhile(status => status != JobStatus.Running).Select(status => status.ToString())],
            [.. audit.OfJob(run.Job).Select(decision =>
                $"{decision.Rule.Match(rule => rule.Name, () => "default")} {decision.Kind} {decision.Target} {decision.Answer} {decision.Delivery}")],
            [.. audit.FormsOfJob(run.Job).Select(form =>
                $"{form.Delivery} {form.Autonomy}" + string.Concat(form.Assumptions.Select(assumption => $" {assumption.Field} {assumption.Basis} {string.Join('|', assumption.Chosen)}")))],
            [.. run.Get<IVerifications>().OfJob(run.Job).Select(report => $"{report.Attempt} {report.Outcome}")],
            await Task.WhenAll(fixture.Files.Select(async file =>
                Outcome.File(file, await File.ReadAllTextAsync(Path.Combine(run.Worktree, file), Cancellation)))));
    }
}
