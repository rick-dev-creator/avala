using System.Globalization;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Simulator.Scenarios;

internal sealed record Conversation(Guid Id, Scenario Scenario, int Played, string Holder)
{
    public static Conversation Begin(Scenario scenario, string holder) => new(Guid.NewGuid(), scenario, 0, holder);

    public IReadOnlyList<IStep> NextScript => Scenario.Script(Played);

    public Conversation Advanced => this with { Played = Played + 1 };

    public ResumeToken Token => new(string.Create(CultureInfo.InvariantCulture, $"{Id:N}/{Scenario.Name}/{Played}/{Holder}"));

    public static Option<ConversationMark> Mark(ResumeToken token) =>
        token.Value.Split('/') is [var id, var name, var played, var holder]
        && Guid.TryParseExact(id, "N", out var conversation)
        && int.TryParse(played, NumberStyles.None, CultureInfo.InvariantCulture, out var turns)
            ? new ConversationMark(conversation, name, turns, holder)
            : Option<ConversationMark>.None;
}

internal sealed record ConversationMark(Guid Id, string Scenario, int Played, string Holder)
{
    public Conversation Resume(Scenario scenario) => new(Id, scenario, Played, Holder);
}
