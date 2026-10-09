using Avala.Sdk.Regions;

namespace Avala.Fixtures.Violating.Regions;

public sealed class GhostRegistration
{
    public RegionName Target { get; } = new("Shell.Ghost");
}
