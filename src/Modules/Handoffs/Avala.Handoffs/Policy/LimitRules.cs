using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Contracts;
using Avala.Sdk;

namespace Avala.Handoffs.Policy;

internal sealed record LimitRules(OnLimit OnLimit, double Threshold, IReadOnlyList<ConnectionName> Connections)
{
    public const double DefaultThreshold = 0.9;

    public static LimitRules Default { get; } = new(OnLimit.Hold, DefaultThreshold, []) { Declared = false };

    public bool Declared { get; init; } = true;

    public ResumeAtReset Resume { get; init; } = ResumeAtReset.Ask;

    public Option<HandoffError> Error { get; init; }

    public static LimitRules Rejected(HandoffError error) => Default with { Error = error, Declared = true };

    public LimitRules Under(Option<HandoffTerms> terms) =>
        Declared ? this : terms.Match(given => this with { OnLimit = OnLimit.HandOffAnyHarness, Connections = given.Connections }, () => this);
}
