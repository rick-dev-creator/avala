using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Triggers.Contracts;

namespace Avala.Host.Tests;

public sealed class TriggerTests(PublishedPlugins plugins)
{
    private const string PassingChecks = """{ "checks": [ { "name": "git", "command": "git", "arguments": ["--version"], "timeoutSeconds": 60 } ] }""";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFixedTimeTriggerOfARepositoryStartsItsJobWhenTheClockReachesItsLocalTimeAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(
            plugins,
            [],
            [(".avala/checks.json", PassingChecks), (".avala/triggers.json", "{}")],
            placed: repository => [("triggers.json", $$"""{ "repositories": [{{Json(repository)}}] }""")]);
        await run.StartedAsync();
        var (at, due) = NextMinute(run);
        await run.Repository.CommitAsync(
            ".avala/triggers.json",
            $$"""{ "triggers": [ { "id": "greet", "schedule": { "at": "{{at}}" }, "instruction": {{Json(SimulatedRun.Simulate("reply"))}} } ] }""",
            Cancellation);
        _ = await run.Get<ITriggers>().ReloadAsync(Cancellation);
        var fired = run.Watch<TriggerFired>();

        run.AdvanceTo(due);

        var trigger = (await fired.UntilAsync(_ => true)).Run;
        Assert.Equal((TriggerOrigin.Schedule, RunOutcome.Submitted, "greet"), (trigger.Origin, trigger.Outcome, trigger.Trigger.Name));
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(Outcomes.Present(trigger.Job)));
        Assert.True(Outcomes.Present(Assert.Single(await run.Get<ITriggers>().ListAsync(Cancellation)).NextRun) > due);
    }

    [Fact]
    public async Task AnIntervalTriggerCatchesUpExactlyOnceAfterAvalaWasClosedAsync()
    {
        await using var run = await MachineAsync(""" "schedule": { "everyMinutes": 60 }, "catchUp": "once" """);
        var trigger = new TriggerId(TriggerId.Machine, "nightly");
        var fired = run.Watch<TriggerFired>();

        run.Clock.Advance(TimeSpan.FromHours(1));
        var first = (await fired.UntilAsync(_ => true)).Run;
        _ = await run.SettledAsync(Outcomes.Present(first.Job));
        await run.RestartAfterAsync(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(10));
        await run.StartedAsync();

        var runs = run.Get<ITriggers>().RunsOf(trigger);
        Assert.Equal([TriggerOrigin.Schedule, TriggerOrigin.CatchUp], runs.Select(made => made.Origin));
        Assert.All(runs, made => Assert.Equal(RunOutcome.Submitted, made.Outcome));
        var next = Outcomes.Present(Assert.Single(await run.Get<ITriggers>().ListAsync(Cancellation)).NextRun);
        Assert.InRange(next - run.Clock.GetUtcNow(), TimeSpan.Zero, TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task WithoutCatchUpRunsMissedWhileClosedAreRecordedAndNoJobStartsAsync()
    {
        await using var run = await MachineAsync(""" "schedule": { "everyMinutes": 60 } """);

        await run.RestartAfterAsync(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(10));
        await run.StartedAsync();

        var missed = Assert.Single(run.Get<ITriggers>().RunsOf(new TriggerId(TriggerId.Machine, "nightly")));
        Assert.Equal((RunOutcome.Missed, 3), (missed.Outcome, missed.Missed));
        Assert.Empty(await run.Get<IJobCatalog>().ListAsync(Cancellation));
    }

    [Fact]
    public async Task ASignedWebhookStartsAJobFromItsPayloadAndABadSignatureOrAReplayIsRefusedAsync()
    {
        var variable = $"AVALA_TEST_HOOK_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, "s3cret");
        await using var run = await MachineAsync(
            $$$"""{ "id": "issue", "repository": "@repository", "webhook": { "secretEnv": "{{{variable}}}" }, "instruction": "{{payload.title}}" }""",
            raw: true);
        var url = Outcomes.Present(run.Get<ITriggers>().Endpoint.Url) + "issue";
        var body = $$"""{ "title": {{Json(SimulatedRun.Simulate("reply"))}} }""";
        using var client = new HttpClient();
        var fired = run.Watch<TriggerFired>();

        var accepted = await PostAsync(client, url, run, body, "n-1", "s3cret");
        var trigger = (await fired.UntilAsync(_ => true)).Run;
        var forged = await PostAsync(client, url, run, body, "n-2", "wrong");
        var replayed = await PostAsync(client, url, run, body, "n-1", "s3cret");

        Assert.Equal((HttpStatusCode.Accepted, HttpStatusCode.Unauthorized, HttpStatusCode.Conflict), (accepted, forged, replayed));
        Assert.Equal((TriggerOrigin.Webhook, RunOutcome.Submitted), (trigger.Origin, trigger.Outcome));
        var job = Outcomes.Present(trigger.Job);
        Assert.Equal(SimulatedRun.Simulate("reply"), Assert.Single(await run.Get<IJobCatalog>().ListAsync(Cancellation)).Instruction);
        Assert.Equal([JobStatus.AwaitingReview], await run.SettledAsync(job));
        var deliveries = run.Get<ITriggers>().Deliveries();
        Assert.Equal([DeliveryVerdict.Accepted, DeliveryVerdict.BadSignature, DeliveryVerdict.Replayed], deliveries.Select(delivery => delivery.Verdict));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(body))), Outcomes.Present(deliveries[0].Digest));
        Assert.Equal(deliveries[0].Digest, trigger.PayloadDigest);
    }

    [Fact]
    public async Task ATriggerCannotRaiseItsJobAboveTheRepositorysAutonomyAsync()
    {
        await using var run = await MachineAsync(""" "schedule": { "everyMinutes": 600 }, "autonomy": "autonomous" """, committed: [(".avala/permissions.json", """{ "autonomy": "supervised", "rules": [] }""")]);

        var trigger = Outcomes.Succeeds(await run.Get<ITriggers>().FireAsync(new TriggerId(TriggerId.Machine, "nightly"), new FireRequest(TriggerOrigin.Manual, "person"), Cancellation));
        var autonomy = await run.AutonomyAsync();

        Assert.Equal((Autonomy.Autonomous, Autonomy.Supervised, true), (trigger.Asked, trigger.Applied, trigger.AutonomyCapped));
        Assert.Equal((Autonomy.Supervised, Outcomes.Present(trigger.Job)), (autonomy.Effective, autonomy.Job));
    }

    [Fact]
    public async Task ATriggerAtItsConcurrencyCapStartsNoSecondJobAsync()
    {
        await using var run = await MachineAsync(""" "schedule": { "everyMinutes": 600 }, "concurrency": 1 """);
        var triggers = run.Get<ITriggers>();
        var nightly = new TriggerId(TriggerId.Machine, "nightly");
        var manual = new FireRequest(TriggerOrigin.Manual, "person");

        var first = Outcomes.Succeeds(await triggers.FireAsync(nightly, manual, Cancellation));
        var second = Outcomes.Succeeds(await triggers.FireAsync(nightly, manual, Cancellation));

        Assert.Equal((RunOutcome.Submitted, RunOutcome.AtConcurrencyCap), (first.Outcome, second.Outcome));
        _ = await run.SettledAsync(Outcomes.Present(first.Job));
        Assert.Single(await run.Get<IJobCatalog>().ListAsync(Cancellation));
    }

    private async Task<SimulatedRun> MachineAsync(string trigger, bool raw = false, IReadOnlyList<(string Path, string Content)>? committed = null)
    {
        var run = await SimulatedRun.PreparedAsync(
            plugins,
            [("resources.json", $$"""{ {{LeasedPorts.Settings(test: 4)}} }""")],
            [(".avala/checks.json", PassingChecks), .. committed ?? []],
            placed: repository =>
            [
                ("triggers.json", raw
                    ? $$"""{ "triggers": [ {{trigger.Replace("\"@repository\"", Json(repository), StringComparison.Ordinal)}} ] }"""
                    : $$"""{ "triggers": [ { "id": "nightly", "repository": {{Json(repository)}}, "instruction": {{Json(SimulatedRun.Simulate("reply"))}}, {{trigger}} } ] }"""),
            ]);
        await run.StartedAsync();

        return run;
    }

    private static async Task<HttpStatusCode> PostAsync(HttpClient client, string url, SimulatedRun run, string body, string nonce, string secret)
    {
        var timestamp = run.Clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var bytes = Encoding.UTF8.GetBytes(body);
        var signed = Encoding.UTF8.GetBytes($"{timestamp}.{nonce}.").Concat(bytes).ToArray();
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(bytes) };
        request.Headers.Add("X-Avala-Timestamp", timestamp);
        request.Headers.Add("X-Avala-Nonce", nonce);
        request.Headers.Add("X-Avala-Signature", Sign(secret, signed));
        using var response = await client.SendAsync(request, Cancellation);

        return response.StatusCode;
    }

    private static string Sign(string secret, byte[] signed) => "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed));

    private static (string At, DateTimeOffset Due) NextMinute(SimulatedRun run)
    {
        var zone = run.Clock.LocalTimeZone;
        var local = TimeZoneInfo.ConvertTime(run.Clock.GetUtcNow(), zone).DateTime;
        var target = new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified).AddMinutes(2);

        return (target.ToString("HH:mm", CultureInfo.InvariantCulture), new DateTimeOffset(target, zone.GetUtcOffset(target)));
    }

    private static string Json(string text) => JsonSerializer.Serialize(text);
}
