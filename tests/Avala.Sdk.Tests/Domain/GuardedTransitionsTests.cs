using Avala.Sdk.Domain;
using Stateless;

namespace Avala.Sdk.Tests.Domain;

public sealed class GuardedTransitionsTests
{
    [Fact]
    public void FiresAPermittedTrigger()
    {
        var door = Door(isLocked: false);

        var result = door.TryFire(Trigger.Open, DoorError.CannotOpen);

        Assert.Equal(DoorState.Open, result.Match(state => state, _ => DoorState.Closed));
        Assert.Equal(DoorState.Open, door.State);
    }

    [Fact]
    public void RejectsATriggerThatIsNotPermitted()
    {
        var door = Door(isLocked: false);

        var result = door.TryFire(Trigger.Close, DoorError.CannotClose);

        Assert.Equal(DoorError.CannotClose, result.Match(_ => DoorError.CannotOpen, error => error));
        Assert.Equal(DoorState.Closed, door.State);
    }

    [Fact]
    public void RejectsATriggerWhoseGuardFails()
    {
        var door = Door(isLocked: true);

        var result = door.TryFire(Trigger.Open, DoorError.CannotOpen);

        Assert.Equal(DoorError.CannotOpen, result.Match(_ => DoorError.CannotClose, error => error));
        Assert.Equal(DoorState.Closed, door.State);
    }

    private static StateMachine<DoorState, Trigger> Door(bool isLocked)
    {
        var machine = new StateMachine<DoorState, Trigger>(DoorState.Closed);

        machine.Configure(DoorState.Closed).PermitIf(Trigger.Open, DoorState.Open, () => !isLocked);
        machine.Configure(DoorState.Open).Permit(Trigger.Close, DoorState.Closed);

        return machine;
    }

    private enum DoorState
    {
        Closed,
        Open,
    }

    private enum Trigger
    {
        Open,
        Close,
    }

    private enum DoorError
    {
        CannotOpen,
        CannotClose,
    }
}
