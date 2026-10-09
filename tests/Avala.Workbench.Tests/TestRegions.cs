using Avala.Sdk;
using Avala.Sdk.Regions;

namespace Avala.Workbench.Tests;

internal sealed class TestRegions : IRegions
{
    private readonly Dictionary<RegionName, Option<object>> contexts = [];
    private readonly Dictionary<RegionName, List<object>> items = [];

    public List<(RegionName Region, Option<object> Context)> Delivered { get; } = [];

    public void Fill(RegionName region, params object[] viewModels)
    {
        items[region] = [.. viewModels];
        Deliver(region, ContextOf(region));
    }

    public Option<object> ContextOf(RegionName region) => contexts.GetValueOrDefault(region);

    public void SetContext<TContext>(RegionName region, TContext context)
        where TContext : notnull =>
        Change(region, Option<object>.Some(context));

    public void ClearContext(RegionName region) => Change(region, Option<object>.None);

    private void Change(RegionName region, Option<object> context)
    {
        contexts[region] = context;
        Delivered.Add((region, context));
        Deliver(region, context);
    }

    private void Deliver(RegionName region, Option<object> context)
    {
        foreach (var item in items.GetValueOrDefault(region, []).OfType<IRegionAware>())
        {
            item.ReceiveContext(context);
        }
    }
}
