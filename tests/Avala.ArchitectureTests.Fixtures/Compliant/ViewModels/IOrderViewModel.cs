namespace Avala.Fixtures.Compliant.ViewModels;

public interface IOrderViewModel
{
    string Summary { get; }

    IOrderLineViewModel Line { get; }
}
