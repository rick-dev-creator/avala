using Avala.Agents.Contracts.Events;
using Avala.Agents.Domain;
using Avala.Testing;

namespace Avala.Agents.Tests.Domain;

public sealed class TurnExpiryTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    [Fact]
    public void ExpiresOnlyItemsSilentForTooLong()
    {
        var turn = Given.Turn(Given.Started("build"));
        Outcomes.Succeeds(turn.Apply(Given.Started("test"), Given.Now.AddMinutes(4)));

        var expired = Outcomes.Succeeds(turn.Expire(Given.Now.AddMinutes(5), Patience)).Events;

        Assert.Equal([new ItemCompleted(Given.Session, Given.TurnId, Given.Item("build"), ItemOutcome.Expired)], expired);
        Assert.Equal([Given.Item("test")], turn.OpenItems);
    }

    [Fact]
    public void AnItemWaitingForPermissionNeverExpires()
    {
        var turn = Given.Turn(Given.Started("deploy"), Given.PermissionFor("deploy"));

        Assert.Empty(Outcomes.Succeeds(turn.Expire(Given.Now.AddHours(8), Patience)).Events);
    }

    [Fact]
    public void AnExpiredItemCannotProgressAnymore()
    {
        var turn = Given.Turn(Given.Started("build"));
        Outcomes.Succeeds(turn.Expire(Given.Now.AddMinutes(5), Patience));

        Assert.Equal(TurnError.ItemAlreadyCompleted, Outcomes.FailsWith(turn.Apply(Given.Progressed("build"), Given.Now.AddMinutes(6))));
    }

    [Fact]
    public void AnEndedTurnHasNothingToExpire() =>
        Assert.Equal(TurnError.TurnEnded, Outcomes.FailsWith(Given.Turn(Given.Ended()).Expire(Given.Now, Patience)));
}
