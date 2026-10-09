using Avala.Sdk;
using Avala.Sdk.Regions;

namespace Avala.Shell.Regions;

internal sealed class Region
{
    public Region(RegionName name, IEnumerable<object> items)
    {
        Name = name;
        Items = [.. items.Distinct()];
    }

    public RegionName Name { get; }

    public IReadOnlyList<object> Items { get; }

    public bool HasItems => Items.Count > 0;

    public static Region Of(RegionName name, IEnumerable<RegionContribution> contributions) =>
        new(name, contributions.Where(contribution => contribution.Region == name)
            .OrderBy(contribution => contribution.Order)
            .Select(contribution => contribution.ViewModel));

    public void Activate()
    {
        foreach (var item in Items.OfType<IActivatable>())
        {
            item.Activate();
        }
    }

    public void Deactivate()
    {
        foreach (var item in Items.OfType<IActivatable>())
        {
            item.Deactivate();
        }
    }

    public void Deliver(Option<object> context)
    {
        foreach (var item in Items.OfType<IRegionAware>())
        {
            item.ReceiveContext(context);
        }
    }
}
