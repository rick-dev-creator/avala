using Avala.Sdk.Appearance;
using Avala.Sdk.Events;

namespace Avala.Workbench.Steering;

internal sealed class SendKeys : IHandle<AppearanceChanged>
{
    private bool withCommand;

    public bool WithEnter => !Volatile.Read(ref withCommand);

    public ValueTask HandleAsync(AppearanceChanged integrationEvent, CancellationToken cancellationToken)
    {
        Volatile.Write(ref withCommand, integrationEvent.Preference.Send == SendKey.CommandEnter);

        return ValueTask.CompletedTask;
    }
}
