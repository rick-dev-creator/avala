using Stateless;

namespace Avala.Agents.Turns;

internal static class TurnLifecycle
{
    public static StateMachine<TurnState, TurnTrigger> Create(Func<TurnState> read, Action<TurnState> write)
    {
        var machine = new StateMachine<TurnState, TurnTrigger>(read, write);

        machine.Configure(TurnState.Live)
            .Permit(TurnTrigger.Finish, TurnState.Finished)
            .Permit(TurnTrigger.Interrupt, TurnState.Interrupted)
            .Permit(TurnTrigger.Fail, TurnState.Failed);

        machine.Configure(TurnState.Working)
            .SubstateOf(TurnState.Live)
            .Permit(TurnTrigger.RequestPermission, TurnState.AwaitingPermission)
            .Permit(TurnTrigger.AskForm, TurnState.AwaitingAnswer);

        machine.Configure(TurnState.AwaitingPermission)
            .SubstateOf(TurnState.Live)
            .Permit(TurnTrigger.ResolvePermission, TurnState.Working);

        machine.Configure(TurnState.AwaitingAnswer)
            .SubstateOf(TurnState.Live)
            .Permit(TurnTrigger.AnswerForm, TurnState.Working);

        return machine;
    }
}
