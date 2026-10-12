using Avala.Sdk.Appearance;

namespace Avala.Workbench.Tests.Onboarding;

internal sealed class FakeOnboarding : IOnboarding
{
    public bool IsDone { get; set; }

    public int Finishes { get; private set; }

    public ValueTask<bool> IsDoneAsync(CancellationToken cancellationToken) => ValueTask.FromResult(IsDone);

    public ValueTask<bool> FinishAsync(CancellationToken cancellationToken)
    {
        IsDone = true;
        Finishes++;

        return ValueTask.FromResult(true);
    }
}
