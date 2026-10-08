using Stateless;

namespace Avala.Fixtures.Violating.Application;

public sealed class StatelessCoordinator
{
    public StateMachine<int, int> Machine { get; } = new(0);
}
