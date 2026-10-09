using System.Text;
using System.Text.Json;
using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Jobs.Launching;
using Avala.Sdk;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.Logging;

namespace Avala.Jobs.JobFiles;

internal sealed partial class JobFileReader(IBaseFiles files, ILogger<JobFileReader> logger) : IRepositoryDefaults
{
    public const string JobFile = ".avala/jobs.json";

    public const int MaximumBytes = 16 * 1024;

    private const string Connection = "connection";

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 2, AllowDuplicateProperties = false };

    public async ValueTask<Result<Option<ConnectionName>, JobRejection>> ConnectionAsync(string worktree, CancellationToken cancellationToken) =>
        (await files.ReadAsync(worktree, JobFile, cancellationToken)).Match(
            file => file.Content.Match(Parse, () => Option<ConnectionName>.None),
            failure => Rejected($"it cannot be read from the base commit: {failure}"));

    private Result<Option<ConnectionName>, JobRejection> Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes)
        {
            return Rejected($"it is over {MaximumBytes} bytes");
        }

        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return Declared(document.RootElement);
        }
        catch (JsonException)
        {
            return Rejected("it is not valid JSON");
        }
    }

    private Result<Option<ConnectionName>, JobRejection> Declared(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Rejected("it is not a JSON object");
        }

        if (root.EnumerateObject().Any(property => property.Name != Connection))
        {
            return Rejected("it has a field the format does not define");
        }

        if (!root.TryGetProperty(Connection, out var named))
        {
            return Option<ConnectionName>.None;
        }

        return named.ValueKind == JsonValueKind.String && named.GetString() is { Length: > 0 } name
            ? Option<ConnectionName>.Some(new ConnectionName(name))
            : Rejected("its connection is not a name");
    }

    private Result<Option<ConnectionName>, JobRejection> Rejected(string reason)
    {
        LogRejected(JobFile, reason);

        return JobRejection.UnusableConnection;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The repository's {File} is rejected because {Reason}")]
    private partial void LogRejected(string file, string reason);
}
