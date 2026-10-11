using Avala.Autopilot.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Automation;

internal sealed class LoopControls(IAutopilot autopilot)
{
    public const int LoopsShown = 10;

    public IReadOnlyList<LoopDigest> Read() =>
    [
        .. autopilot.Loops()
            .OrderBy(loop => loop.Status == LoopStatus.Ended)
            .ThenByDescending(loop => loop.Started)
            .Take(LoopsShown)
            .SelectMany(loop => autopilot.DigestOf(loop.Loop).Match<LoopDigest[]>(digest => [digest], () => [])),
    ];

    public ValueTask<Result<LoopId, AutopilotError>> PauseAsync(LoopId loop, CancellationToken cancellationToken) => autopilot.PauseAsync(loop, cancellationToken);

    public ValueTask<Result<LoopId, AutopilotError>> ResumeAsync(LoopId loop, CancellationToken cancellationToken) => autopilot.ResumeAsync(loop, cancellationToken);

    public ValueTask<Result<LoopId, AutopilotError>> StopAsync(LoopId loop, CancellationToken cancellationToken) => autopilot.StopAsync(loop, cancellationToken);
}
