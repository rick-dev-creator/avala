using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Checks;

internal sealed class CheckFileFormat : IRuleFileFormat
{
    public string Path => CheckDeclaration.RelativePath;

    public Option<Enum> Rejection(string content) =>
        CheckDeclaration.Parse(content).Match(_ => Option<Enum>.None, error => Option<Enum>.Some(error));
}
