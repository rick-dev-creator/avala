using Avala.Agents.Connections;
using Avala.Agents.Contracts.Connections;
using Avala.Sdk;

namespace Avala.Agents.ConnectionFiles;

internal sealed class ConnectionFileReader(AvalaPaths paths) : IConnectionFile
{
    public const string FileName = "connections.json";

    public const int MaximumBytes = 64 * 1024;

    private Task<Result<Option<ConnectionDeclarations>, ConnectionError>>? loading;

    public async ValueTask<Result<Option<ConnectionDeclarations>, ConnectionError>> LoadAsync(CancellationToken cancellationToken) =>
        await LazyInitializer.EnsureInitialized(ref loading, () => ReadAsync(Path.Combine(paths.Data, FileName))).WaitAsync(cancellationToken);

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
