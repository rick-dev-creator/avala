using Avala.Sdk;

namespace Avala.Agents.Contracts.Sessions;

public sealed record ProviderInfo(string Id, string Name)
{
    public bool OffersImplicitConnection { get; init; } = true;

    public Option<string> Install { get; init; }

    public Option<string> SignIn { get; init; }
}
