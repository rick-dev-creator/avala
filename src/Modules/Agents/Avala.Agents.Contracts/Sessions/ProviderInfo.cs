namespace Avala.Agents.Contracts.Sessions;

public sealed record ProviderInfo(string Id, string Name)
{
    public bool OffersImplicitConnection { get; init; } = true;
}
