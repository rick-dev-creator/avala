using Avala.Agents.Contracts.Connections;
using Avala.Sdk;

namespace Avala.Agents.Credentials;

internal sealed class LoginFolderSource(AvalaPaths paths) : ICredentialSource
{
    public const string Name = "login";

    public const string FolderName = "connections";

    public string Source => Name;

    public ValueTask<Result<ConnectionEnvironment, ConnectionError>> ResolveAsync(CredentialRequest request, CancellationToken cancellationToken)
    {
        var folder = request.Reference.Match(
            reference => Path.GetFullPath(reference, paths.Data),
            () => Path.Combine(paths.Folder(FolderName), request.Connection.Value));

        return ValueTask.FromResult(Directory.Exists(folder)
            ? Result<ConnectionEnvironment, ConnectionError>.Success(new ConnectionEnvironment { ConfigurationDirectory = folder })
            : ConnectionError.MissingFolder);
    }
}
