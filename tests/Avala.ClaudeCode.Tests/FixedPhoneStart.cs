using Avala.ClaudeCode.Conversations;

namespace Avala.ClaudeCode.Tests;

internal sealed class FixedPhoneStart(bool startsOn = false, bool saves = true) : IPhoneStart
{
    public bool StartsOn { get; private set; } = startsOn;

    public Task<bool> ChooseAsync(bool on, CancellationToken cancellationToken)
    {
        StartsOn = saves ? on : StartsOn;

        return Task.FromResult(saves);
    }
}
