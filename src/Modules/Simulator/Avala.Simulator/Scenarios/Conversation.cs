using System.Globalization;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Simulator.Scenarios;

internal sealed record Conversation(Guid Id, Scenario Scenario, int Played)
{
    public static Conversation Begin(Scenario scenario) => new(Guid.NewGuid(), scenario, 0);

    public IReadOnlyList<IStep> NextScript => Scenario.Script(Played);

    public Conversation Advanced => this with { Played = Played + 1 };

    public ResumeToken Token => new(string.Create(CultureInfo.InvariantCulture, $"{Id:N}/{Scenario.Name}/{Played}"));

    public static Option<ConversationMark> Mark(ResumeToken token) =>
        token.Value.Split('/') is [var id, var name, var played]
        && Guid.TryParseExact(id, "N", out var conversation)
        && int.TryParse(played, NumberStyles.None, CultureInfo.InvariantCulture, out var turns)
            ? new ConversationMark(conversation, name, turns)
            : Option<ConversationMark>.None;
}

internal sealed record ConversationMark(Guid Id, string Scenario, int Played)
{
    public Conversation Resume(Scenario scenario) => new(Id, scenario, Played);
}
