using System.Globalization;
using System.Text;
using Avala.Jobs.Contracts;
using Avala.Notes.Contracts;
using Avala.Sdk;

namespace Avala.Notes.Notebook;

internal static class NoteDocument
{
    private const string Fence = "---";

    public static string Written(NoteRecord note)
    {
        var text = new StringBuilder()
            .Append(Fence).Append('\n')
            .Append("id: ").Append(note.Id.Value.ToString("D")).Append('\n')
            .Append("state: ").Append(note.State.ToString().ToLowerInvariant()).Append('\n');

        foreach (var job in note.Place.Job.Match<JobId[]>(found => [found], () => []))
        {
            text.Append("job: ").Append(job.Value.ToString("D")).Append('\n');
        }

        text.Append("created: ").Append(note.Created.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append("updated: ").Append(note.Updated.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append(Fence).Append("\n\n")
            .Append("# ").Append(note.Title).Append('\n');

        if (note.Body.Length > 0)
        {
            text.Append('\n').Append(note.Body).Append('\n');
        }

        return text.ToString();
    }

    public static Option<NoteRecord> Read(string content, string repository, IReadOnlyList<NoteMedia> media)
    {
        var lines = content.ReplaceLineEndings("\n").Split('\n');

        if (lines.Length < 2 || lines[0] != Fence || Array.IndexOf(lines, Fence, 1) is not (> 0 and var closing))
        {
            return Option<NoteRecord>.None;
        }

        var fields = lines[1..closing]
            .Select(line => line.Split(':', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0].Trim(), pair => pair[1].Trim(), StringComparer.Ordinal);
        var rest = lines[(closing + 1)..].SkipWhile(line => line.Trim().Length == 0).ToList();

        if (rest.Count == 0 || !rest[0].StartsWith("# ", StringComparison.Ordinal)
            || !Guid.TryParse(Field(fields, "id"), out var id)
            || !Enum.TryParse<NoteState>(Field(fields, "state"), ignoreCase: true, out var state)
            || !DateTimeOffset.TryParse(Field(fields, "created"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var created)
            || !DateTimeOffset.TryParse(Field(fields, "updated"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var updated))
        {
            return Option<NoteRecord>.None;
        }

        var place = Guid.TryParse(Field(fields, "job"), out var job) ? NotePlace.OfJob(new JobId(job), repository) : NotePlace.OfProject(repository);
        var body = string.Join('\n', rest.Skip(1)).Trim('\n');

        return new NoteRecord(new NoteId(id), place, rest[0][2..].Trim(), body, media, state, created, updated);
    }

    private static string Field(Dictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var value) ? value : string.Empty;
}
