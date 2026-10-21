using Avala.Jobs.Contracts;

namespace Avala.Delegation.Policy;

internal static class ChildRetries
{
    public const int Most = 2;

    public static bool Retries(HoldReason reason) => reason is HoldReason.SessionLost or HoldReason.TurnFailed;
}
