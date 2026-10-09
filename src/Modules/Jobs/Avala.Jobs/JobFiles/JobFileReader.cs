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

    private const string Approval = "approval";

    private const string Autopilot = "autopilot";

    private const string Delegation = "delegation";

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 3, AllowDuplicateProperties = false };

    public async ValueTask<Result<Option<ConnectionName>, JobRejection>> ConnectionAsync(string worktree, CancellationToken cancellationToken) =>
        (await DeclaredAsync(worktree, cancellationToken))
            .Map(declared => declared.Connection.Map(name => new ConnectionName(name)))
            .MapError(_ => JobRejection.UnusableConnection);

    public async ValueTask<Result<Option<string>, JobRejection>> ApprovalAsync(string worktree, CancellationToken cancellationToken) =>
        (await DeclaredAsync(worktree, cancellationToken)).Map(declared => declared.Approval);

    private async Task<Result<Declaration, JobRejection>> DeclaredAsync(string worktree, CancellationToken cancellationToken) =>
        (await files.ReadAsync(worktree, JobFile, cancellationToken)).Match(
            file => file.Content.Match(Parse, () => new Declaration(Option<string>.None, Option<string>.None)),
            failure => Rejected($"it cannot be read from the base commit: {failure}"));

    private Result<Declaration, JobRejection> Parse(string text)
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

    private Result<Declaration, JobRejection> Declared(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Rejected("it is not a JSON object");
        }

        if (root.EnumerateObject().Any(property => property.Name is not (Connection or Approval or Autopilot or Delegation)))
        {
            return Rejected("it has a field the format does not define");
        }

        if (root.TryGetProperty(Autopilot, out var autopilot) && !IsSection(autopilot, listsAllowed: false))
        {
            return Rejected($"its {Autopilot} is not an object of plain values");
        }

        if (root.TryGetProperty(Delegation, out var delegation) && !IsSection(delegation, listsAllowed: true))
        {
            return Rejected($"its {Delegation} is not an object of plain values and lists of them");
        }

        return Named(root, Connection).Bind(connection => Named(root, Approval).Map(approval => new Declaration(connection, approval)));
    }

    private static bool IsSection(JsonElement section, bool listsAllowed) =>
        section.ValueKind == JsonValueKind.Object
        && section.EnumerateObject().All(field => field.Value.ValueKind switch
        {
            JsonValueKind.Object => false,
            JsonValueKind.Array => listsAllowed && field.Value.EnumerateArray().All(item => item.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)),
            _ => true,
        });

    private Result<Option<string>, JobRejection> Named(JsonElement root, string field)
    {
        if (!root.TryGetProperty(field, out var named))
        {
            return Option<string>.None;
        }

        return named.ValueKind == JsonValueKind.String && named.GetString() is { Length: > 0 } name
            ? Option<string>.Some(name)
            : Rejected($"its {field} is not a name");
    }

    private JobRejection Rejected(string reason)
    {
        LogRejected(JobFile, reason);

        return JobRejection.InvalidJobFile;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The repository's {File} is rejected because {Reason}")]
    private partial void LogRejected(string file, string reason);

    private sealed record Declaration(Option<string> Connection, Option<string> Approval);
}
