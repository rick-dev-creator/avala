using System.Text.Json;
using System.Text.Json.Serialization;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Mentioning;

namespace Avala.Workbench.Drafting.Storage;

internal sealed class DraftFile(AvalaPaths paths) : IDraftStore
{
    public const string FileName = "drafts.json";

    private const long MaximumBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private string FilePath => Path.Combine(paths.Data, FileName);

    public async Task<IReadOnlyDictionary<JobId, Draft>> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > MaximumBytes)
            {
                return new Dictionary<JobId, Draft>();
            }

            await using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            var kept = await JsonSerializer.DeserializeAsync<List<KeptDraft>>(stream, Options, cancellationToken) ?? [];

            return kept.Where(draft => draft.Job != Guid.Empty).ToDictionary(draft => new JobId(draft.Job), draft => new Draft(draft.Text, draft.Attachments, draft.Mentions) { Waiting = [.. draft.Waiting.Select(message => new WaitingMessage(new MessageId(message.Id), message.Text, message.Attachments, message.Returned) { Why = Undelivered(message) })] });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Dictionary<JobId, Draft>();
        }
    }

    public async Task WriteAsync(IReadOnlyDictionary<JobId, Draft> drafts, CancellationToken cancellationToken)
    {
        var temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(paths.Data);
            var kept = drafts.Select(pair => new KeptDraft(pair.Key.Value) { Text = pair.Value.Text, Attachments = pair.Value.Attachments, Mentions = pair.Value.Mentions, Waiting = [.. pair.Value.Waiting.Select(message => new KeptMessage(message.Id.Value) { Text = message.Text, Attachments = message.Attachments, Returned = message.Returned, Why = message.Why.Match<KeptWhy[]>(why => [new KeptWhy(why.Status) { Rejection = why.Rejection.Match<JobRejection[]>(rejection => [rejection], () => []) }], () => []) })] }).ToList();
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(kept, Options), cancellationToken);
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            File.Delete(temporary);
        }
    }

    private static Option<UndeliveredMessage> Undelivered(KeptMessage message) =>
        message.Why is [var kept, ..]
            ? new UndeliveredMessage(kept.Status, kept.Rejection is [var rejection, ..] ? rejection : Option<JobRejection>.None)
            : Option<UndeliveredMessage>.None;

    private sealed record KeptDraft(Guid Job)
    {
        public string Text { get; init; } = string.Empty;

        public IReadOnlyList<Attachment> Attachments { get; init; } = [];

        public IReadOnlyList<Mention> Mentions { get; init; } = [];

        public IReadOnlyList<KeptMessage> Waiting { get; init; } = [];
    }

    private sealed record KeptMessage(Guid Id)
    {
        public string Text { get; init; } = string.Empty;

        public IReadOnlyList<Attachment> Attachments { get; init; } = [];

        public bool Returned { get; init; }

        public IReadOnlyList<KeptWhy> Why { get; init; } = [];
    }

    private sealed record KeptWhy(JobStatus Status)
    {
        public IReadOnlyList<JobRejection> Rejection { get; init; } = [];
    }
}
