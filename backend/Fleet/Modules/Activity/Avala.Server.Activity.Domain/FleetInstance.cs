namespace Avala.Server.Activity.Domain;

internal sealed class FleetInstance
{
    private readonly List<CostTotal> costs = [];

    private FleetInstance(Guid id, string name, DateTimeOffset lastSeenAt)
    {
        Id = id;
        Name = name;
        LastSeenAt = lastSeenAt;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    public TokenTotals Tokens { get; private set; } = new();

    public IReadOnlyCollection<CostTotal> Costs => costs;

    public static FleetInstance Register(Guid id, string name, DateTimeOffset at) => new(id, name, at);

    public void See(string name, DateTimeOffset at)
    {
        Name = name;
        LastSeenAt = at > LastSeenAt ? at : LastSeenAt;
    }

    public void Spend(TokenCounts tokens, CostAmount? cost)
    {
        Tokens.Add(tokens);

        if (cost is not null)
        {
            CostTotal.Add(costs, cost);
        }
    }
}
