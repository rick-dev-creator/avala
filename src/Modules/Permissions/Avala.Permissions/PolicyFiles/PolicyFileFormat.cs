using System.Text;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.PolicyFiles;

internal sealed class PolicyFileFormat : IRuleFileFormat
{
    public string Path => PermissionPolicy.PolicyFile;

    public Option<Enum> Rejection(string content) =>
        Encoding.UTF8.GetByteCount(content) > PolicyFileReader.MaximumBytes
            ? Option<Enum>.Some(PolicyError.TooLarge)
            : PolicyFileParser.Parse(content).Match(_ => Option<Enum>.None, error => Option<Enum>.Some(error));
}
