using System.Globalization;
using System.Text.Json.Nodes;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

[UsesRealGit]
public sealed class RealResumeRuns(PublishedPlugins plugins)
{
    private const string Gate = "AVALA_REAL_RESUME";

    private const string Branch = "wip/notes-and-visual-evidence";

    private const string Instruction =
        "This branch holds a feature that an earlier job left after 9 of its 18 plan steps. Its plan is in docs/features/necesito-un-nuevo-feature-avala/ (latest v10-as-built.md). "
        + "Steps 2, 5, 6, 12, 13, 14, 15, 16 and 18 are left; step 2 was started and is not verified. Resume the work: finish step 2 first, then go on step by step, keeping every test green. "
        + "When a step is done, say which one and stop for review.";

    private const string GoOn = "Go on with the next unfinished step of the plan. When every step is done, say so.";

    private static readonly TimeSpan Settling = TimeSpan.FromMinutes(90);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Login => Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE_LOGIN") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-work");

    private static string Model => Environment.GetEnvironmentVariable("AVALA_REAL_MODEL") is { Length: > 0 } model ? model : "sonnet";

    private static int Rounds => int.TryParse(Environment.GetEnvironmentVariable("AVALA_REAL_ROUNDS"), CultureInfo.InvariantCulture, out var rounds) ? rounds : 6;

    private static string Output => Environment.GetEnvironmentVariable("AVALA_REAL_OUTPUT") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Path.GetTempPath(), "avala-real-resume");

    [Fact]
    public async Task TheOvernightJobsWorkResumedWithRealClaudeCodeRunsRoundAfterRoundWithoutAvalaFailingItAsync()
    {
        var source = Environment.GetEnvironmentVariable(Gate) ?? string.Empty;
        Assert.SkipUnless(Directory.Exists(source), $"Set {Gate} to a clone of Avala that has {Branch}, AVALA_REAL_CLAUDE_LOGIN, and optionally AVALA_REAL_MODEL, AVALA_REAL_ROUNDS, AVALA_REAL_OUTPUT and AVALA_REAL_KEEP (a branch of that clone to keep the approved work in), to resume the overnight job with real Claude Code.");
        var transcripts = Path.Combine(Output, "transcripts");
        Directory.CreateDirectory(transcripts);
        var log = new List<string>();
        await using var run = await SimulatedRun.PreparedAsync(plugins, [("connections.json", Connections(transcripts))], [], harnesses: true);
        await run.Repository.GitAsync(Cancellation, "fetch", "--quiet", source, Branch);
        await run.Repository.GitAsync(Cancellation, "reset", "--quiet", "--hard", "FETCH_HEAD");
        await File.WriteAllTextAsync(Path.Combine(run.Repository.Path, ".avala", "permissions.json"), """{ "autonomy": "autonomous" }""", Cancellation);
        await File.WriteAllTextAsync(Path.Combine(run.Repository.Path, ".avala", "jobs.json"), """{ "approval": "merge" }""", Cancellation);
        await run.Repository.GitAsync(Cancellation, "add", "--all");
        await run.Repository.GitAsync(Cancellation, "commit", "--quiet", "-m", "Run the resumed job autonomously");
        var started = run.Clock.GetUtcNow();
        var real = System.Diagnostics.Stopwatch.StartNew();
        using var pace = TimeProvider.System.CreateTimer(_ => run.AdvanceTo(started + real.Elapsed), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        var job = Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, Instruction)));
        var ending = string.Empty;

        for (var round = 1; round <= Rounds && ending.Length == 0; round++)
        {
            var history = await SettledAsync(run, job);
            var summary = history.Summary;
            Note(log, $"round {round}: {summary.Status} hold {summary.Hold.Match(hold => hold.ToString(), () => "none")} attempts {history.Attempts.Count} failure {summary.Failure.Match(failure => failure.ToString(), () => "none")}");
            ending = summary.Status switch
            {
                JobStatus.Failed or JobStatus.Discarded or JobStatus.Approved => summary.Status.ToString(),
                JobStatus.AwaitingReview when round == Rounds => "last round",
                JobStatus.AwaitingReview => await NextAsync(log, run.Get<IJobs>().SendBackAsync(job, GoOn, Cancellation)),
                JobStatus.NeedsHelp => await NextAsync(log, run.Get<IJobs>().ContinueAsync(job, GoOn, Cancellation)),
                _ => $"unsettled {summary.Status}",
            };
        }

        await KeepAsync(run, job, source, log);
        await File.WriteAllLinesAsync(Path.Combine(Output, "avala-view.txt"), log, Cancellation);
        Assert.NotEqual(JobStatus.Failed.ToString(), ending);
    }

    private static async Task KeepAsync(SimulatedRun run, JobId job, string source, List<string> log)
    {
        if (Environment.GetEnvironmentVariable("AVALA_REAL_KEEP") is not { Length: > 0 } branch)
        {
            return;
        }

        Note(log, $"approved: {(await run.Get<IJobs>().ApproveAsync(job, Cancellation)).Match(_ => "yes", rejection => rejection.ToString())}");
        await run.Repository.GitAsync(Cancellation, "push", "--quiet", "--force", source, $"HEAD:refs/heads/{branch}");
        Note(log, $"kept in {branch} of {source}");
    }

    private static async Task<string> NextAsync<T>(List<string> log, ValueTask<Result<T, JobRejection>> sent)
        where T : notnull =>
        (await sent).Match(_ => string.Empty, rejection =>
        {
            Note(log, $"the message was refused: {rejection}");

            return $"refused {rejection}";
        });

    private static void Note(List<string> log, string line) =>
        log.Add($"{DateTimeOffset.UtcNow:O} {line}");

    private static async Task<JobHistory> SettledAsync(SimulatedRun run, JobId job)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        guard.CancelAfter(Settling);

        while (true)
        {
            var progressed = run.Watch<JobProgressed>();
            var history = Outcomes.Present(await run.Get<IJobCatalog>().HistoryAsync(job, guard.Token));

            if (history.Summary.Status is not JobStatus.Running and not JobStatus.Preparing and not JobStatus.Checking)
            {
                return history;
            }

            try
            {
                _ = await progressed.UntilAsync(found => found.Job == job && found.Status is not JobStatus.Running and not JobStatus.Preparing and not JobStatus.Checking).WaitAsync(guard.Token);
            }
            catch (TimeoutException)
            {
                guard.Token.ThrowIfCancellationRequested();
            }
        }
    }

    private static string Connections(string transcripts) => new JsonObject
    {
        ["default"] = "claude",
        ["connections"] = new JsonArray(new JsonObject
        {
            ["name"] = "claude",
            ["provider"] = "claude-code",
            ["credential"] = new JsonObject { ["source"] = "login", ["reference"] = Login },
            ["settings"] = new JsonObject { ["model"] = Model, ["transcripts"] = transcripts },
        }),
    }.ToJsonString();
}
