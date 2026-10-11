using System.Globalization;
using System.Security.Cryptography;
using Avala.Testing.UI;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal sealed class DogfoodCampaign(PublishedPlugins plugins, HeadlessUi ui)
{
    private const double RehearsedThreshold = 0.3;

    public async Task RunAsync(DogfoodScenario scenario)
    {
        Assert.SkipUnless(Gated && Chosen(scenario.Name, scenario.Optional), $"Set {Gate}=1 and AVALA_DOGFOOD_SCENARIO={scenario.Name} (or all) to dogfood {scenario.Name}; add AVALA_DOGFOOD_REHEARSAL=1 to rehearse it on the simulator.");
        Assert.SkipUnless(HeadlessApp.Renders, "Set AVALA_HEADLESS_RENDER=1 so the dogfood run can take screenshots.");
        var ledger = new DogfoodLedger(Campaign);
        var folder = Directory.CreateDirectory(Path.Combine(Campaign, $"{scenario.Name}-{DateTime.Now:yyyyMMdd-HHmmss}")).FullName;
        var place = new DogfoodPlace(folder, Directory.CreateDirectory(Path.Combine(folder, "screens")).FullName);
        var started = DateTimeOffset.Now;
        var evidence = new DogfoodEvidence();
        List<string> header = [];
        DogfoodReadiness? readiness = null;

        await using (var journal = new DogfoodJournal(place.Report))
        {
            var refusal = (await ledger.RefusalAsync(scenario)).Match(found => $"REFUSED by the campaign ledger: {found}", () => string.Empty);

            if (refusal.Length == 0 && !Rehearsing)
            {
                readiness = await DogfoodPreflight.CheckAsync(scenario, journal);
                refusal = readiness.Refusals.Count > 0 ? $"REFUSED by the preflight: {string.Join("; ", readiness.Refusals)}" : string.Empty;
                header.AddRange(readiness.Accounts.Select(account => $"Preflight: {account}"));
            }

            header.Add($"Avala commit {(await DogfoodRepository.RunAsync("git", ["-C", AppContext.BaseDirectory, "rev-parse", "HEAD"], new Dictionary<string, string?>())).Output.Trim()}");
            header.Add($"Harness: {(Rehearsing ? "the simulator (rehearsal, no cost)" : readiness?.Version)}; connections {string.Join(", ", DogfoodRepository.Declared(scenario).Select(declared => $"{declared.Name} {declared.Model} on {declared.Login}"))}");

            if (refusal.Length > 0)
            {
                await journal.NoteAsync(refusal);
                evidence.Outcome = refusal;
            }
            else
            {
                var setup = new DogfoodSetup(place, Rehearsing, Rehearsing ? RehearsedThreshold : readiness!.Threshold, scenario.Cap);
                header.Add(string.Create(CultureInfo.InvariantCulture, $"Limit threshold for this run: {setup.Threshold}"));
                await ledger.ReserveAsync(scenario, place);
                Environment.SetEnvironmentVariable(HookSecret, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24)));
                await DogfoodRepository.PrepareAsync(journal, scenario, setup);
                await ui.RunAsync(() => PlayAsync(scenario, setup, journal, evidence), Cancellation);
                await DogfoodRepository.AssertLocalAsync(journal, place);
            }
        }

        var result = Judge(scenario, place, started, evidence, await DogfoodReport.FlaggedAsync(place));

        if (evidence.Outcome.StartsWith("REFUSED", StringComparison.Ordinal))
        {
            await DogfoodReport.WriteAsync(result, scenario, place, header, (await ledger.ReadAsync()).Committed);
            await DogfoodReport.CampaignAsync(Campaign, await ledger.ReadAsync());
            Assert.Fail(evidence.Outcome);
        }

        await ledger.SettleAsync(place, evidence.Spent, evidence.Outcome);
        var book = await ledger.ReadAsync();
        await DogfoodReport.WriteAsync(result, scenario, place, header, book.Committed);
        await DogfoodReport.CampaignAsync(Campaign, book);
        var missed = result.Rows.Where(row => row.Mark != DogfoodMark.Pass).Select(row => $"{row.Mark}: {row.Then} — {row.Evidence}").ToList();
        Assert.True(result.Passed, $"{scenario.Name} {evidence.Outcome}; see {Path.Combine(place.Report, "report.md")}{Environment.NewLine}{string.Join(Environment.NewLine, missed)}");
    }

    private async Task PlayAsync(DogfoodScenario scenario, DogfoodSetup setup, DogfoodJournal journal, DogfoodEvidence evidence)
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        guard.CancelAfter(Rehearsing ? TimeSpan.FromMinutes(3) : scenario.Guard);
        var camera = new DogfoodCamera(journal, setup.Place.Screens);
        await using var app = new DogfoodApp(plugins, setup.Place, journal, camera);
        var actions = new DogfoodActions(app, journal, camera, evidence);
        var scene = new DogfoodScene(new DogfoodStage(scenario, setup, journal, guard.Token), app, actions, evidence);
        await app.ComposeAsync();

        try
        {
            await camera.ShootAsync("first-run");
            await scenario.Steps(scene);
        }
        catch (DogfoodStoppedException stopped)
        {
            evidence.Outcome = evidence.Outcome == "completed" ? stopped.Message : evidence.Outcome;
            await journal.NoteAsync(stopped.Message);
            await camera.ShootAsync("zz-stopped");
        }
        catch (Exception failure) when (failure is not OperationCanceledException || !Cancellation.IsCancellationRequested)
        {
            evidence.Outcome = $"DRIVER FAILURE: {failure.GetType().Name}: {failure.Message}";
            await journal.NoteAsync($"DRIVER FAILURE: {failure}");
            await camera.ShootAsync("zz-driver-failure");
        }
        finally
        {
            await scene.DrainAsync();
            await app.StopAsync();
            await DogfoodLeftovers.LateEventsAsync(journal, evidence, setup.Place.Data);
            await journal.NoteAsync(string.Create(CultureInfo.InvariantCulture, $"Total reported cost {evidence.Spent} USD, {app.Clock.Elapsed:hh\\:mm\\:ss}, outcome {evidence.Outcome}"));
        }
    }

    private static DogfoodResult Judge(DogfoodScenario scenario, DogfoodPlace place, DateTimeOffset started, DogfoodEvidence evidence, IReadOnlyList<string> flagged)
    {
        var refused = evidence.Outcome.StartsWith("REFUSED", StringComparison.Ordinal);
        List<DogfoodRow> rows = [.. scenario.Checks.Select(check => refused ? new DogfoodRow(check.Then, DogfoodMark.NotReached, evidence.Outcome) : Row(check, evidence))];

        return new DogfoodResult(
            scenario.Name,
            scenario.Title,
            Rehearsing ? "rehearsal" : "real",
            place.Folder,
            started,
            DateTimeOffset.Now,
            evidence.Outcome,
            scenario.Cap,
            evidence.Spent,
            evidence.SpentBy(evidence.ConnectionOf),
            evidence.SpentBy(evidence.AccountOf),
            rows,
            [.. evidence.Findings, .. flagged]);
    }

    private static DogfoodRow Row(DogfoodCheck check, DogfoodEvidence evidence)
    {
        try
        {
            var verdict = check.Judge(evidence);

            return new DogfoodRow(check.Then, verdict.Mark, verdict.Evidence);
        }
        catch (Exception failure) when (failure is InvalidOperationException or ArgumentException or KeyNotFoundException or FormatException)
        {
            return new DogfoodRow(check.Then, DogfoodMark.Fail, $"the check could not read its evidence: {failure.Message}");
        }
    }
}
