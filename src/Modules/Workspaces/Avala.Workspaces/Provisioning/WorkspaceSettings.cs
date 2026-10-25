using Avala.Sdk;

namespace Avala.Workspaces.Provisioning;

internal sealed record WorkspaceSettings(string Root)
{
    public string Root { get; } = Root.Canonical();

    public string Kept => Path.Combine(Path.GetDirectoryName(Root) ?? Root, "shelved");
}
