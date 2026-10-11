using Avala.Autopilot.Contracts;
using Avala.Sdk;

namespace Avala.Autopilot.Loops;

internal sealed record LoopLimitsDeclaration
{
    public static LoopLimitsDeclaration None { get; } = new();

    public Option<int> Iterations { get; init; }

    public Option<int> FailuresInARow { get; init; }

    public Option<int> SameFailure { get; init; }

    public Option<int> NothingChanged { get; init; }

    public Option<double> PauseAtLimit { get; init; }

    public LoopLimits Setting(LoopLimits defaults) => defaults with
    {
        Iterations = Iterations.Match(declared => declared, () => defaults.Iterations),
        FailuresInARow = FailuresInARow.Match(declared => declared, () => defaults.FailuresInARow),
        SameFailure = SameFailure.Match(declared => declared, () => defaults.SameFailure),
        NothingChanged = NothingChanged.Match(declared => declared, () => defaults.NothingChanged),
        PauseAtLimit = PauseAtLimit.Match(declared => declared, () => defaults.PauseAtLimit),
    };

    public LoopLimits Tightening(LoopLimits ceiling) => ceiling with
    {
        Iterations = Iterations.Match(declared => Math.Min(declared, ceiling.Iterations), () => ceiling.Iterations),
        FailuresInARow = FailuresInARow.Match(declared => Math.Min(declared, ceiling.FailuresInARow), () => ceiling.FailuresInARow),
        SameFailure = SameFailure.Match(declared => Math.Min(declared, ceiling.SameFailure), () => ceiling.SameFailure),
        NothingChanged = NothingChanged.Match(declared => Math.Min(declared, ceiling.NothingChanged), () => ceiling.NothingChanged),
        PauseAtLimit = PauseAtLimit.Match(declared => Math.Min(declared, ceiling.PauseAtLimit), () => ceiling.PauseAtLimit),
    };
}
