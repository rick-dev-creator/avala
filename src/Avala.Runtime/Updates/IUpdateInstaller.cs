using Avala.Sdk;

namespace Avala.Runtime.Updates;

internal sealed record InstallerFinding(bool Reached, Option<string> Newer)
{
    public static InstallerFinding Unreachable { get; } = new(false, Option<string>.None);
}

internal interface IUpdateInstaller
{
    Option<string> Downloaded { get; }

    Task<InstallerFinding> FindAsync(CancellationToken cancellationToken);

    Task<bool> DownloadAsync(CancellationToken cancellationToken);

    void ApplyOnExit();
}
