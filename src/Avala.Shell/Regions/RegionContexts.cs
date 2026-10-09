using Avala.Sdk;
using Avala.Sdk.Regions;

namespace Avala.Shell.Regions;

internal sealed class RegionContexts : IRegions
{
    private readonly Dictionary<RegionName, Option<object>> contexts = [];
    private readonly Dictionary<RegionName, Region> regions = [];

    public event Action<RegionName>? Delivered;

    public void Attach(Region region)
    {
        regions[region.Name] = region;
        region.Deliver(ContextOf(region.Name));
    }

    public Option<object> ContextOf(RegionName region) => contexts.GetValueOrDefault(region);

    public void SetContext<TContext>(RegionName region, TContext context)
        where TContext : notnull =>
        Change(region, Option<object>.Some(context));

    public void ClearContext(RegionName region) => Change(region, Option<object>.None);

    private void Change(RegionName region, Option<object> context)
    {
        contexts[region] = context;

        if (regions.TryGetValue(region, out var attached))
        {
            attached.Deliver(context);
            Delivered?.Invoke(region);
        }
    }
}
