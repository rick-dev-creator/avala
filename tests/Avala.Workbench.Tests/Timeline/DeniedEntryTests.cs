using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Tests.Cards;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Timeline;

public sealed class DeniedEntryTests
{
    private readonly Denying denying = new();

    [Fact]
    public void AnAutonomousDenialAddsADeniedEntryAfterItsRequestWhichAnAllowAnswerMarksAllowed()
    {
        var decision = denying.Decision();
        var denied = denying.Transcript(decision);
        var allowed = denied.Apply(new HumanAnswer(denying.Session, denying.Job, new ItemId("fetch"), ItemKind.Web, Denying.Style, PermissionAnswer.Allow, Option<string>.None, Denying.HostRule, DateTimeOffset.UnixEpoch));

        Assert.Equal(decision, Assert.IsType<DeniedEntry>(denied.Entries[^1]).Decision);
        Assert.True(Assert.IsType<DeniedEntry>(allowed.Entries[^1]).Allowed.IsSome);
    }

    [Fact]
    public void ASupervisedDenialOrADecisionLeftToAPersonAddsNoDeniedEntry()
    {
        var supervised = denying.Transcript(denying.Decision(Autonomy.Supervised));
        var asked = denying.Transcript(denying.Decision() with { Answer = PolicyAnswer.Ask, Delivery = DecisionDelivery.LeftToHuman });

        Assert.Empty(supervised.Entries.OfType<DeniedEntry>());
        Assert.Empty(asked.Entries.OfType<DeniedEntry>());
    }
}
