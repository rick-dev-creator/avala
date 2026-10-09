using System.Text.Json;
using Avala.Testing;

namespace Avala.Host.Tests;

internal sealed record RegressionFixture(string? Record, IReadOnlyList<(string Path, string Content)> Committed, IReadOnlyList<string> Files, Outcome Expected)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow };

    public static async Task<RegressionFixture> LoadAsync(string name, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(RecordingFixtures.ExpectationsOf(name), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        var declared = await JsonSerializer.DeserializeAsync<Declared>(stream, Options, cancellationToken)
            ?? throw new InvalidOperationException($"{name} declares no expectations.");
        var files = declared.Files ?? [];

        return new RegressionFixture(
            declared.Record,
            [.. (declared.Repository ?? []).Select(file => (file.Key, file.Value))],
            [.. files.Keys],
            new Outcome(
                declared.Journey ?? [],
                declared.Permissions ?? [],
                declared.Forms ?? [],
                declared.Verifications ?? [],
                [.. files.Select(file => Outcome.File(file.Key, file.Value))]));
    }

    private sealed record Declared(
        string? Record,
        Dictionary<string, string>? Repository,
        string[]? Journey,
        string[]? Permissions,
        string[]? Forms,
        string[]? Verifications,
        Dictionary<string, string>? Files);
}

internal sealed record Outcome(
    IReadOnlyList<string> Journey,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Forms,
    IReadOnlyList<string> Verifications,
    IReadOnlyList<string> Files)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string File(string path, string content) => $"{path}: {content}";

    public bool Equals(Outcome? other) =>
        other is not null
        && Journey.SequenceEqual(other.Journey)
        && Permissions.SequenceEqual(other.Permissions)
        && Forms.SequenceEqual(other.Forms)
        && Verifications.SequenceEqual(other.Verifications)
        && Files.SequenceEqual(other.Files);

    public override int GetHashCode() => HashCode.Combine(Journey.Count, Permissions.Count, Forms.Count, Verifications.Count, Files.Count);

    public override string ToString() =>
        JsonSerializer.Serialize(new { Journey, Permissions, Forms, Verifications, Files }, Indented);
}
