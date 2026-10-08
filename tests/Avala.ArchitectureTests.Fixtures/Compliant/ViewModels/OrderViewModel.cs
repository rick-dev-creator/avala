using Avala.Fixtures.Compliant.Application;

namespace Avala.Fixtures.Compliant.ViewModels;

public sealed class OrderViewModel(IStartOrder startOrder)
{
    public string Summary { get; private set; } = string.Empty;

    public async ValueTask StartAsync(string line, CancellationToken cancellationToken) =>
        Summary = (await startOrder.ExecuteAsync(line, cancellationToken))
            .Match(summary => $"{summary.Lines} lines", failure => failure.ToString());
}
