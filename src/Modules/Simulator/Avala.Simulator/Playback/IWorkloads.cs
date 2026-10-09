using Avala.Sdk.Processes;
using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Playback;

internal interface IWorkloads
{
    Task<string> StartAsync(IProcessLauncher launcher, Workload workload, string workingDirectory, CancellationToken cancellationToken);
}
