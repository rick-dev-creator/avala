using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Contracts;
using Avala.Sdk;

namespace Avala.Handoffs.Policy;

internal sealed record LimitRules(OnLimit OnLimit, double Threshold, IReadOnlyList<ConnectionName> Connections)
{
    public const double DefaultThreshold = 0.9;

    public const double DefaultHardCap = 0.98;

    public static LimitRules Default { get; } = new(OnLimit.Hold, DefaultThreshold, []) { Declared = false };

    public bool Declared { get; init; } = true;

    public ResumeAtReset Resume { get; init; } = ResumeAtReset.Ask;

    public double HardCap { get; init; } = DefaultHardCap;

    public HandoffStrategy Strategy { get; init; } = HandoffStrategy.MostHeadroom;

    public bool WindDown { get; init; } = true;

    public bool CompactOnResume { get; init; } = true;

    public Option<HandoffError> Error { get; init; }

    public static LimitRules Rejected(HandoffError error) => Default with { Error = error, Declared = true };

    public static LimitRules Of(LimitSettings settings) =>
        new(settings.OnLimit, settings.Threshold, settings.Connections)
        {
            Resume = settings.Resume,
            HardCap = settings.HardCap,
            Strategy = settings.Strategy,
            WindDown = settings.WindDown,
            CompactOnResume = settings.CompactOnResume,
        };

    public LimitSettings Settings() =>
        new(OnLimit, Threshold, HardCap, Connections) { Strategy = Strategy, Resume = Resume, WindDown = WindDown, CompactOnResume = CompactOnResume };

    public LimitFile File() =>
        new(Settings(), !Declared ? LimitFileStatus.Absent : Error.IsSome ? LimitFileStatus.Rejected : LimitFileStatus.Applied) { Problem = Error };

    public LimitRules ForCompanion() => this with { OnLimit = OnLimit.Hold, Connections = [], Resume = ResumeAtReset.Auto };

    public LimitRules Under(Option<HandoffTerms> terms) =>
        terms.Match(given => (Declared || given.Connections.Count == 0 ? this : this with { OnLimit = OnLimit.HandOffAnyHarness, Connections = given.Connections }) with { Resume = ResumeAtReset.Auto }, () => this);
}
