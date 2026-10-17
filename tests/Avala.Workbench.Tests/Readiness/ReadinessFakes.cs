using Avala.Forges.Contracts;

namespace Avala.Workbench.Tests.Readiness;

internal sealed class FakeForgeReadiness : IForgeReadiness
{
    public ForgeReadiness Shown { get; set; } = new(ForgeReadinessKind.NoRemote, string.Empty);

    public ValueTask<ForgeReadiness> OfAsync(string repository, CancellationToken cancellationToken) => ValueTask.FromResult(Shown);
}
