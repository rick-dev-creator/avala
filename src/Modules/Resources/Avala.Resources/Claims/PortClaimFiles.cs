using System.Collections.Immutable;
using System.Globalization;
using Avala.Resources.Leasing;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Resources.Claims;

internal sealed class PortClaimFiles(IListeningPorts listening, string folder) : IPortClaims
{
    public static string MachineFolder => Path.Combine(Path.GetTempPath(), "avala-ports");

    public async ValueTask<IReadOnlySet<int>> ListenedAsync(CancellationToken cancellationToken) =>
        (await listening.ListAsync(ImmutableHashSet<int>.Empty, cancellationToken)).Select(listener => listener.Port).ToHashSet();

    public Option<IDisposable> TryClaim(int first, int last)
    {
        Directory.CreateDirectory(folder);
        var held = new List<FileStream>();

        if (Enumerable.Range(first, last - first + 1).All(port => Claim(port, held)))
        {
            return new Held(held);
        }

        held.ForEach(file => file.Dispose());

        return Option<IDisposable>.None;
    }

    private bool Claim(int port, List<FileStream> held)
    {
        try
        {
            held.Add(new FileStream(Path.Combine(folder, string.Create(CultureInfo.InvariantCulture, $"{port}.claim")), new FileStreamOptions
            {
                Mode = FileMode.OpenOrCreate,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
            }));

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class Held(List<FileStream> files) : IDisposable
    {
        public void Dispose() => files.ForEach(file => file.Dispose());
    }
}
