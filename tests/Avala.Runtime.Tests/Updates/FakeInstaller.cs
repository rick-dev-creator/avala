using Avala.Runtime.Updates;
using Avala.Sdk;

namespace Avala.Runtime.Tests.Updates;

internal sealed class FakeInstaller : IUpdateInstaller
{
    public InstallerFinding Finding { get; init; } = new(true, Option<string>.None);

    public bool Downloads { get; init; } = true;

    public Option<string> Downloaded { get; set; } = Option<string>.None;

    public int Finds { get; private set; }

    public int Downloading { get; private set; }

    public int Applied { get; private set; }

    public static FakeInstaller Offering(string version) => new() { Finding = new InstallerFinding(true, version) };

    public Task<InstallerFinding> FindAsync(CancellationToken cancellationToken)
    {
        Finds++;

        return Task.FromResult(Finding);
    }

    public Task<bool> DownloadAsync(CancellationToken cancellationToken)
    {
        Downloading++;

        if (Downloads)
        {
            Downloaded = Finding.Newer;
        }

        return Task.FromResult(Downloads);
    }

    public void ApplyOnExit() => Applied++;
}
