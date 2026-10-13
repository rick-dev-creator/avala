using System.Text;
using System.Text.Json;
using Avala.Contests.Contracts;
using Avala.Contests.Running;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Contests.RepositoryFiles;

internal sealed class BestOfReader(IBaseFiles files) : IBestOfFile
{
    public const string JobFile = ".avala/jobs.json";

    public const string Section = "bestOf";

    public const int MaximumBytes = 16 * 1024;

    private static readonly string[] Fields = ["candidates", "max"];

    private const int SectionDepth = 1;

    public async Task<Result<BestOf, ContestRejection>> OfRepositoryAsync(string repository, CancellationToken cancellationToken) =>
        (await files.ReadCurrentAsync(repository.Trim(), JobFile, cancellationToken)).Match(
            file => file.Content.Match(Parse, () => BestOf.Default),
            _ => ContestRejection.Unreadable);

    public static Result<BestOf, ContestRejection> Parse(string text) =>
        Encoding.UTF8.GetByteCount(text) > MaximumBytes ? ContestRejection.TooLarge
        : JsonSections.Read(text, Section, SectionDepth, ContestRejection.Malformed, section => section.Match(Declared, () => BestOf.Default));

    private static Result<BestOf, ContestRejection> Declared(JsonElement section)
    {
        if (section.ValueKind != JsonValueKind.Object)
        {
            return ContestRejection.Malformed;
        }

        if (section.EnumerateObject().Any(field => !Fields.Contains(field.Name)))
        {
            return ContestRejection.UnknownField;
        }

        return Count(section, "max", BestOf.Default.Max, BestOf.Limit, ContestRejection.InvalidMax)
            .Bind(max => Count(section, "candidates", BestOf.Default.Candidates, max, ContestRejection.InvalidCandidates)
                .Map(candidates => new BestOf(candidates, max)));
    }

    private static Result<int, ContestRejection> Count(JsonElement section, string field, int fallback, int most, ContestRejection invalid) =>
        !section.TryGetProperty(field, out var value) ? Math.Min(fallback, most)
        : value.ValueKind != JsonValueKind.Number ? ContestRejection.Malformed
        : value.TryGetInt32(out var count) && count >= 1 && count <= most ? count
        : invalid;
}
