using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.PolicyFiles;

internal sealed class PolicyFileReader : IPolicyFiles
{
    public const int MaximumBytes = 64 * 1024;

    public async ValueTask<Result<Option<IReadOnlyList<PolicyRule>>, PolicyError>> ReadAsync(
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(workingDirectory, PermissionPolicy.PolicyFile);

        if (!File.Exists(path))
        {
            return Option<IReadOnlyList<PolicyRule>>.None;
        }

        return (await ReadTextAsync(path, cancellationToken))
            .Bind(PolicyFileParser.Parse)
            .Map(Option<IReadOnlyList<PolicyRule>>.Some);
    }

    private static async Task<Result<string, PolicyError>> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > MaximumBytes)
            {
                return PolicyError.TooLarge;
            }

            using var reader = new StreamReader(stream);

            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return PolicyError.Unreadable;
        }
    }
}
