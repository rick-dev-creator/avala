namespace Avala.Server.Activity.Domain;

internal sealed class QuarterUsage
{
    private readonly List<CostTotal> costs = [];

    private QuarterUsage(Guid instanceId, Guid jobId, DateTimeOffset quarter, string model)
    {
        InstanceId = instanceId;
        JobId = jobId;
        Quarter = quarter;
        Model = model;
    }

    public static Guid OutsideAnyJob { get; } = Guid.Empty;

    public Guid InstanceId { get; }

    public Guid JobId { get; }

    public DateTimeOffset Quarter { get; }

    public string Model { get; }

    public TokenTotals Tokens { get; private set; } = new();

    public IReadOnlyCollection<CostTotal> Costs => costs;

    public static QuarterUsage Start(Guid instanceId, Guid jobId, DateTimeOffset quarter, string model) => new(instanceId, jobId, quarter, model);

    public void Spend(TokenCounts tokens, CostAmount? cost)
    {
        Tokens.Add(tokens);

        if (cost is not null)
        {
            CostTotal.Add(costs, cost);
        }
    }
}
