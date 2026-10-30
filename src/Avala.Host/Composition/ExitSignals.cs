using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Avala.Host.Composition;

internal sealed class ExitSignals(CrashLog crashes, Action stop) : IDisposable
{
    public static readonly ImmutableArray<PosixSignal> Handled = [PosixSignal.SIGTERM, PosixSignal.SIGHUP, PosixSignal.SIGINT];

    private ImmutableArray<PosixSignalRegistration> registrations = [];

    public void Watch() => registrations = [.. Handled.Select(signal => PosixSignalRegistration.Create(signal, Receive))];

    public void Receive(PosixSignalContext context)
    {
        context.Cancel = true;
        crashes.Signalled(context.Signal);
        stop();
    }

    public void Dispose()
    {
        foreach (var registration in registrations)
        {
            registration.Dispose();
        }
    }
}
