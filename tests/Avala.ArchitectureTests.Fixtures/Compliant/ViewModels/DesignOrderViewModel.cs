namespace Avala.Fixtures.Compliant.ViewModels;

public sealed class DesignOrderViewModel : IOrderViewModel
{
    public string Summary => "3 lines";

    public IOrderLineViewModel Line { get; } = new DesignOrderLineViewModel();
}
