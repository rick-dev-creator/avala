using Avala.Agents.Connections;
using Avala.Agents.Contracts.Connections;
using Avala.Sdk;

namespace Avala.Agents.ConnectionFiles;

internal sealed class ConnectionFileReader(AvalaPaths paths) : IConnectionFile, IAsyncDisposable
{
    public const string FileName = "connections.json";

    public const int MaximumBytes = 64 * 1024;

    private const string Empty = "{}";

    private readonly SerialExecutor writes = new();
    private Task<Result<Option<ConnectionDeclarations>, ConnectionError>>? loading;

    private string FilePath => Path.Combine(paths.Data, FileName);

    public async ValueTask<Result<Option<ConnectionDeclarations>, ConnectionError>> LoadAsync(CancellationToken cancellationToken) =>
        await LazyInitializer.EnsureInitialized(ref loading, () => ReadAsync(FilePath)).WaitAsync(cancellationToken);

    public async ValueTask<Result<ConnectionDeclarations, ConnectionError>> ChangeDefaultAsync(
        Option<ConnectionName> connection,
        CancellationToken cancellationToken) =>
        await writes.RunAsync(token => WriteAsync(connection, token), cancellationToken);

    public ValueTask DisposeAsync() => writes.DisposeAsync();

    private async Task<Result<ConnectionDeclarations, ConnectionError>> WriteAsync(Option<ConnectionName> connection, CancellationToken cancellationToken)
    {
        var current = File.Exists(FilePath) ? await ReadTextAsync(FilePath) : Empty;

        return await current
            .Bind(text => ConnectionFileParser.WithDefault(text, connection))
            .BindAsync(text => SaveAsync(text, cancellationToken));
    }

    private async Task<Result<ConnectionDeclarations, ConnectionError>> SaveAsync(string text, CancellationToken cancellationToken)
    {
        if (!ConnectionFileParser.Parse(text).TryGetValue(out var declarations, out var error))
        {
            return error;
        }

        var temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(paths.Data);
            await File.WriteAllTextAsync(temporary, text, cancellationToken);
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ConnectionError.Unwritable;
        }

        Volatile.Write(ref loading, Task.FromResult(Result<Option<ConnectionDeclarations>, ConnectionError>.Success(declarations)));

        return declarations;
    }

    private static async Task<Result<Option<ConnectionDeclarations>, ConnectionError>> ReadAsync(string path) =>
        !File.Exists(path)
            ? Option<ConnectionDeclarations>.None
            : (await ReadTextAsync(path)).Bind(ConnectionFileParser.Parse).Map(Option<ConnectionDeclarations>.Some);

    private static async Task<Result<string, ConnectionError>> ReadTextAsync(string path)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return ConnectionError.TooLarge;
            }

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ConnectionError.Unreadable;
        }
    }
}
