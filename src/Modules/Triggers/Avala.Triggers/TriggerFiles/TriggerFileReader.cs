using Avala.Sdk;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Triggers.TriggerFiles;

internal sealed class TriggerFileReader(IBaseFiles files, AvalaPaths paths) : ITriggerFiles
{
    public const string RepositoryFile = ".avala/triggers.json";

    public const string MachineFile = "triggers.json";

    public async ValueTask<FileReading> MachineAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(paths.Data, MachineFile);
        var text = await TextAsync(path, cancellationToken);

        return text.Match(
            read => read.Match(
                content => TriggerFileParser.ParseMachine(content).Match(
                    parsed => new FileReading(new TriggerFile(path, TriggerFileStatus.Applied) { Triggers = parsed.Triggers.Count }, parsed.Triggers, parsed.Repositories),
                    error => Rejected(path, Option<string>.None, error)),
                () => new FileReading(new TriggerFile(path, TriggerFileStatus.Absent), [], [])),
            error => Rejected(path, Option<string>.None, error));
    }

    public async ValueTask<FileReading> RepositoryAsync(string repository, CancellationToken cancellationToken)
    {
        var path = Path.Combine(repository, RepositoryFile);

        return (await files.ReadCurrentAsync(repository, RepositoryFile, cancellationToken)).Match(
            file => file.Content.Match(
                content => TriggerFileParser.ParseRepository(content, repository).Match(
                    parsed => new FileReading(new TriggerFile(path, TriggerFileStatus.Applied) { Repository = repository, Triggers = parsed.Count }, parsed, []),
                    error => Rejected(path, repository, error)),
                () => new FileReading(new TriggerFile(path, TriggerFileStatus.Absent) { Repository = repository }, [], [])),
            _ => Rejected(path, repository, TriggerError.Unreadable));
    }

    private static FileReading Rejected(string path, Option<string> repository, TriggerError error) =>
        new(new TriggerFile(path, TriggerFileStatus.Rejected) { Repository = repository, Error = error }, [], []);

    private static async Task<Result<Option<string>, TriggerError>> TextAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return Option<string>.None;
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > TriggerFileParser.MaximumBytes)
            {
                return TriggerError.TooLarge;
            }

            using var reader = new StreamReader(stream);

            return Option<string>.Some(await reader.ReadToEndAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return TriggerError.Unreadable;
        }
    }
}
