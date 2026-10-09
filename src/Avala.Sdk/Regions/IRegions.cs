namespace Avala.Sdk.Regions;

public interface IRegions
{
    void SetContext<TContext>(RegionName region, TContext context)
        where TContext : notnull;

    void ClearContext(RegionName region);
}
