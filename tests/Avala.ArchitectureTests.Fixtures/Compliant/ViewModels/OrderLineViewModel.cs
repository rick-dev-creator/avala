namespace Avala.Fixtures.Compliant.ViewModels;

public sealed class OrderLineViewModel(string text) : IOrderLineViewModel
{
    public string Text { get; } = text;
}
