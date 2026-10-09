using Avala.Agents.Contracts.Capabilities;

namespace Avala.Fixtures.Violating.Contracts;

public sealed record SettableCapability : ICapability
{
    public string Mode { get; set; } = "fast";
}
