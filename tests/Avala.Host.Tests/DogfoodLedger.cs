using System.Globalization;
using System.Text.Json;
using Avala.Sdk;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal sealed record DogfoodEntry(string Scenario, string Folder, decimal Cap, decimal Spent, string Status, DateTimeOffset At);

internal sealed record DogfoodBook(decimal Cap, IReadOnlyList<DogfoodEntry> Entries)
{
    public const string Running = "running";

    public decimal Committed => Entries.Sum(entry => entry.Status == Running ? Math.Max(entry.Cap, entry.Spent) : entry.Spent);

    public decimal Spent => Entries.Sum(entry => entry.Spent);
}

internal sealed class DogfoodLedger(string campaign)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Path => System.IO.Path.Combine(campaign, "ledger.json");

    public async Task<DogfoodBook> ReadAsync() =>
        File.Exists(Path)
            ? JsonSerializer.Deserialize<DogfoodBook>(await File.ReadAllTextAsync(Path, Cancellation), Options) ?? new DogfoodBook(CampaignCap, [])
            : new DogfoodBook(CampaignCap, []);

    public async Task<Option<string>> RefusalAsync(DogfoodScenario scenario)
    {
        var book = await ReadAsync();
        var cap = Math.Min(CampaignCap, book.Cap);

        return book.Committed >= cap
            ? Money($"the campaign ledger already holds {book.Committed} USD of its {cap} USD cap, so the campaign is over")
            : book.Committed + scenario.Cap > cap
                ? Money($"the campaign ledger holds {book.Committed} USD and {scenario.Name}'s cap of {scenario.Cap} USD would take it over the campaign cap of {cap} USD")
                : Option<string>.None;
    }

    public Task ReserveAsync(DogfoodScenario scenario, DogfoodPlace place) =>
        ChangeAsync(book => book with { Entries = [.. book.Entries, new DogfoodEntry(scenario.Name, place.Folder, scenario.Cap, 0m, DogfoodBook.Running, DateTimeOffset.Now)] });

    public Task SettleAsync(DogfoodPlace place, decimal spent, string status) =>
        ChangeAsync(book => book with
        {
            Entries = [.. book.Entries.Select(entry => entry.Folder == place.Folder ? entry with { Spent = spent, Status = status, At = DateTimeOffset.Now } : entry)],
        });

    private async Task ChangeAsync(Func<DogfoodBook, DogfoodBook> change)
    {
        Directory.CreateDirectory(campaign);
        var changed = change(await ReadAsync());
        var temporary = Path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(changed, Options), Cancellation);
        File.Move(temporary, Path, overwrite: true);
    }

    private static string Money(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
