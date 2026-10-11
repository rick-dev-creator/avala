using System.Globalization;
using System.Text;
using System.Text.Json;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal sealed record DogfoodRow(string Then, DogfoodMark Mark, string Evidence);

internal sealed record DogfoodResult(
    string Scenario,
    string Title,
    string Mode,
    string Folder,
    DateTimeOffset Started,
    DateTimeOffset Ended,
    string Outcome,
    decimal Cap,
    decimal Spent,
    IReadOnlyDictionary<string, decimal> PerConnection,
    IReadOnlyDictionary<string, decimal> PerAccount,
    IReadOnlyList<DogfoodRow> Rows,
    IReadOnlyList<string> Findings)
{
    public bool Passed => Rows.Count > 0 && Rows.All(row => row.Mark == DogfoodMark.Pass);
}

internal static class DogfoodReport
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private static readonly string[] Flags = ["FINDING", "SLOW", "NOT SHOWN", "DRIVER FAILURE", "DRIVER STOP", "GUARD", "REFUSED"];

    public static async Task WriteAsync(DogfoodResult result, DogfoodScenario scenario, DogfoodPlace place, IReadOnlyList<string> header, decimal ledger)
    {
        var report = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"# {scenario.Name}: {scenario.Title}")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"- Feature: {scenario.Feature}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Mode: {result.Mode}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Outcome: {result.Outcome}")
            .AppendLine(CultureInfo.InvariantCulture, $"- Started {result.Started:u}, ended {result.Ended:u}, took {result.Ended - result.Started:hh\\:mm\\:ss}");

        foreach (var line in header)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"- {line}");
        }

        report.AppendLine().AppendLine("## Acceptance").AppendLine().AppendLine("| Then | Result | Evidence |").AppendLine("|---|---|---|");

        foreach (var row in result.Rows)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"| {Cell(row.Then)} | {Mark(row.Mark)} | {Cell(row.Evidence)} |");
        }

        report.AppendLine().AppendLine("## Cost").AppendLine()
            .AppendLine(Money($"- Reported, API-equivalent: {result.Spent} USD of the scenario cap {result.Cap} USD; campaign ledger after it: {ledger} USD of {CampaignCap} USD"));

        foreach (var (connection, amount) in result.PerConnection)
        {
            report.AppendLine(Money($"- On {connection}: {amount} USD"));
        }

        foreach (var (account, amount) in result.PerAccount)
        {
            report.AppendLine(Money($"- Account {account}: {amount} USD"));
        }

        await AppendLogsAsync(report, place);
        report.AppendLine().AppendLine("## Screenshots").AppendLine();

        foreach (var shot in Directory.Exists(place.Screens) ? Directory.GetFiles(place.Screens, "*.png").Order(StringComparer.Ordinal).ToArray() : [])
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"- [{Path.GetFileName(shot)}]({shot}) · [texts]({Path.ChangeExtension(shot, ".txt")})");
        }

        report.AppendLine().AppendLine("## Surprises and findings").AppendLine();

        foreach (var finding in result.Findings)
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"- {finding}");
        }

        await File.WriteAllTextAsync(Path.Combine(place.Report, "report.md"), report.ToString(), Cancellation);
        await File.WriteAllTextAsync(Path.Combine(place.Folder, "result.json"), JsonSerializer.Serialize(result, Options), Cancellation);
    }

    public static async Task<IReadOnlyList<string>> FlaggedAsync(DogfoodPlace place)
    {
        var notes = Path.Combine(place.Report, "notes.log");

        return File.Exists(notes)
            ? [.. (await File.ReadAllLinesAsync(notes, Cancellation)).Where(line => Flags.Any(flag => line.Contains(flag, StringComparison.Ordinal)))]
            : [];
    }

    public static async Task CampaignAsync(string campaign, DogfoodBook book)
    {
        var results = new List<DogfoodResult>();

        foreach (var file in Directory.GetFiles(campaign, "result.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            results.Add(JsonSerializer.Deserialize<DogfoodResult>(await File.ReadAllTextAsync(file, Cancellation), Options)!);
        }

        var summary = new StringBuilder()
            .AppendLine("# Dogfood campaign")
            .AppendLine()
            .AppendLine(Money($"Ledger: {book.Spent} USD spent, {book.Committed} USD committed, of the {Math.Min(book.Cap, CampaignCap)} USD cap."))
            .AppendLine()
            .AppendLine("| Scenario | Mode | Outcome | Acceptance | Spent (USD) | Report |")
            .AppendLine("|---|---|---|---|---|---|");

        foreach (var result in results)
        {
            var counts = string.Join(", ", result.Rows.GroupBy(row => row.Mark).Select(group => $"{group.Count()} {Mark(group.Key)}"));
            summary.AppendLine(Money($"| {result.Scenario}: {Cell(result.Title)} | {result.Mode} | {Cell(result.Outcome)} | {counts} | {result.Spent} | [report]({Path.Combine(result.Folder, "report", "report.md")}) |"));
        }

        summary.AppendLine().AppendLine("## Spent per account").AppendLine();

        foreach (var account in results.SelectMany(result => result.PerAccount).GroupBy(pair => pair.Key, StringComparer.Ordinal))
        {
            summary.AppendLine(Money($"- {account.Key}: {account.Sum(pair => pair.Value)} USD"));
        }

        summary.AppendLine().AppendLine("## Failed or unreached acceptance").AppendLine();

        foreach (var result in results)
        {
            foreach (var row in result.Rows.Where(row => row.Mark != DogfoodMark.Pass))
            {
                summary.AppendLine(CultureInfo.InvariantCulture, $"- {result.Scenario} ({result.Mode}) {Mark(row.Mark)}: {row.Then} — {row.Evidence}");
            }
        }

        summary.AppendLine().AppendLine("## Findings").AppendLine();

        foreach (var finding in results.SelectMany(result => result.Findings.Select(finding => $"{result.Scenario}: {finding}")).Distinct(StringComparer.Ordinal))
        {
            summary.AppendLine(CultureInfo.InvariantCulture, $"- {finding}");
        }

        await File.WriteAllTextAsync(Path.Combine(campaign, "campaign.md"), summary.ToString(), Cancellation);
    }

    private static async Task AppendLogsAsync(StringBuilder report, DogfoodPlace place)
    {
        report.AppendLine().AppendLine("## Transcript excerpts");

        foreach (var log in (string[])["decisions.log", "permissions.log", "commands.log", "harness-tools.log"])
        {
            var path = Path.Combine(place.Report, log);
            var lines = File.Exists(path) ? await File.ReadAllLinesAsync(path, Cancellation) : [];
            report.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"### {log} ({lines.Length} lines)").AppendLine().AppendLine("```");

            foreach (var line in lines.Take(40))
            {
                report.AppendLine(line.Length > 400 ? string.Concat(line.AsSpan(0, 400), "…") : line);
            }

            report.AppendLine("```");
        }

        report.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"Full logs: {place.Report}; recordings: {Path.Combine(place.Data, "recordings")}; Claude Code transcripts: {Path.Combine(place.Folder, "transcripts")}");
    }

    private static string Mark(DogfoodMark mark) => mark switch
    {
        DogfoodMark.Pass => "PASS",
        DogfoodMark.Fail => "FAIL",
        _ => "NOT REACHED",
    };

    private static string Cell(string text)
    {
        var flat = text.ReplaceLineEndings(" ⏎ ").Replace("|", "\\|", StringComparison.Ordinal);

        return flat.Length > 600 ? string.Concat(flat.AsSpan(0, 600), "…") : flat;
    }

    private static string Money(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
