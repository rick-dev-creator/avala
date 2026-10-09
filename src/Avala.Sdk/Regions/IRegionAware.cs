namespace Avala.Sdk.Regions;

public interface IRegionAware
{
    void ReceiveContext(Option<object> context);
}

public interface IRegionAware<TContext> : IRegionAware
    where TContext : notnull
{
    void OnRegionContextChanged(Option<TContext> context);

    void IRegionAware.ReceiveContext(Option<object> context) =>
        OnRegionContextChanged(context.Bind(value => value is TContext typed ? Option<TContext>.Some(typed) : Option<TContext>.None));
}
