using Stateless;

namespace Avala.Sdk.Domain;

public static class GuardedTransitions
{
    extension<TState, TTrigger>(StateMachine<TState, TTrigger> machine)
    {
        public Result<TState, TError> TryFire<TError>(TTrigger trigger, TError rejection)
            where TError : struct, Enum
        {
            if (!machine.CanFire(trigger))
            {
                return Result<TState, TError>.Failure(rejection);
            }

            machine.Fire(trigger);

            return Result<TState, TError>.Success(machine.State);
        }
    }
}
