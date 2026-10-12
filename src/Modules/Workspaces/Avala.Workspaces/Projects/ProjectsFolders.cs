using System.Text.Json;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workspaces.Projects;

internal sealed record ProjectsSettings(string Home, string File)
{
    public const string FileName = "workspaces.json";

    public static IReadOnlyList<string> Usual { get; } = ["Projects", "projects", "code", "src", "dev", Path.Combine("source", "repos")];
}

internal sealed class ProjectsFolders(ProjectsSettings settings) : IProjectsFolder, IAsyncDisposable
{
    private const string Field = "projectsFolder";

    private readonly SerialExecutor writes = new();
    private Task<Option<string>>? chosen;

    public async ValueTask<ProjectsFolder> ReadAsync(CancellationToken cancellationToken) =>
        new(Suggested(), await LazyInitializer.EnsureInitialized(ref chosen, ReadChosenAsync).WaitAsync(cancellationToken));

    public async ValueTask<Result<ProjectsFolder, WorkspaceFailure>> ChooseAsync(string folder, CancellationToken cancellationToken)
    {
        var trimmed = folder.Trim();

        if (!Path.IsPathFullyQualified(trimmed))
        {
            return WorkspaceFailure.RelativeFolder;
        }

        if (!Directory.Exists(trimmed))
        {
            return WorkspaceFailure.FolderMissing;
        }

        var kept = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));

        return await writes.RunAsync(token => WriteAsync(kept, token), cancellationToken);
    }

    public ValueTask DisposeAsync() => writes.DisposeAsync();

    private string Suggested() =>
        ProjectsSettings.Usual.Select(name => Path.Combine(settings.Home, name)).FirstOrDefault(Directory.Exists) ?? settings.Home;

    private async Task<Result<ProjectsFolder, WorkspaceFailure>> WriteAsync(string folder, CancellationToken cancellationToken)
    {
        var written = settings.File + ".writing";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settings.File)!);
            await File.WriteAllTextAsync(written, JsonSerializer.Serialize(new Dictionary<string, string> { [Field] = folder }) + "\n", cancellationToken);
            File.Move(written, settings.File, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return WorkspaceFailure.FileUnwritable;
        }

        Volatile.Write(ref chosen, Task.FromResult(Option<string>.Some(folder)));

        return new ProjectsFolder(Suggested(), folder);
    }

    private async Task<Option<string>> ReadChosenAsync()
    {
        try
        {
            if (!File.Exists(settings.File))
            {
                return Option<string>.None;
            }

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(settings.File, CancellationToken.None));

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(Field, out var value)
                && value.ValueKind == JsonValueKind.String
                && value.GetString() is { } folder
                && Path.IsPathFullyQualified(folder)
                ? folder
                : Option<string>.None;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return Option<string>.None;
        }
    }
}
