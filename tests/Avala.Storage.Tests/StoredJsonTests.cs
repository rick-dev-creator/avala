using Avala.Sdk;

namespace Avala.Storage.Tests;

public sealed class StoredJsonTests
{
    [Fact]
    public void ARecordWithPresentAndAbsentOptionsReadsBackEqual()
    {
        var written = new Reading(new Identifier(Guid.CreateVersion7()), Shade.Dark, Option<int>.Some(3), Option<Identifier>.None, ["a", "b"])
        {
            Nested = new Reading(new Identifier(Guid.CreateVersion7()), Shade.Light, Option<int>.None, new Identifier(Guid.CreateVersion7()), []),
        };

        var read = StoredJson.Read<Reading>(StoredJson.Write(written));

        Assert.Equal(StoredJson.Write(written), StoredJson.Write(read));
        Assert.Equal((written.Id, written.Shade, written.Count, written.Other), (read.Id, read.Shade, read.Count, read.Other));
        Assert.Equal(written.Labels, read.Labels);
        Assert.Equal(written.Nested.Map(nested => nested.Other), read.Nested.Map(nested => nested.Other));
    }

    [Fact]
    public void AnOptionMissingFromTheStoredTextReadsAsAbsent()
    {
        var read = StoredJson.Read<Reading>("""{ "Id": { "Value": "0199c3a1-7a10-7000-8000-000000000001" }, "Shade": "Light", "Labels": [] }""");

        Assert.Equal((Option<int>.None, Option<Identifier>.None, Option<Reading>.None), (read.Count, read.Other, read.Nested));
    }

    [Fact]
    public void EnumsAreStoredByName() =>
        Assert.Contains("\"Shade\":\"Dark\"", StoredJson.Write(new Reading(default, Shade.Dark, Option<int>.None, Option<Identifier>.None, [])), StringComparison.Ordinal);

    public enum Shade
    {
        Light,
        Dark,
    }

    public readonly record struct Identifier(Guid Value);

    public sealed record Reading(Identifier Id, Shade Shade, Option<int> Count, Option<Identifier> Other, IReadOnlyList<string> Labels)
    {
        public Option<Reading> Nested { get; init; }
    }
}
