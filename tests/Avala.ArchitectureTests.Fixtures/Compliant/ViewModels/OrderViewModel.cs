using Avala.Fixtures.Compliant.Application;

namespace Avala.Fixtures.Compliant.ViewModels;

public sealed class OrderViewModel(StartOrder startOrder, IOrderLineViewModel line) : IOrderViewModel
{
    public string Summary { get; private set; } = string.Empty;

    public IOrderLineViewModel Line { get; } = line;

    public async ValueTask StartAsync(string text, CancellationToken cancellationToken) =>
        Summary = (await startOrder.ExecuteAsync(text, cancellationToken))
            .Match(summary => $"{summary.Lines} lines", failure => failure.ToString());
}
