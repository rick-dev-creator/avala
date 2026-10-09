using System.Text;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.PolicyFiles;

internal sealed class PolicyFileFormat : IRuleFileFormat
{
    public string Path => PermissionPolicy.PolicyFile;

    public Option<RuleFileRejection> Rejection(string content) =>
        Encoding.UTF8.GetByteCount(content) > PolicyFileReader.MaximumBytes
            ? Rejected(PolicyError.TooLarge)
            : PolicyFileParser.Parse(content).Match(_ => Option<RuleFileRejection>.None, Rejected);

    private static Option<RuleFileRejection> Rejected(PolicyError error) => RuleFileRejection.Of("Permissions", error);
}
