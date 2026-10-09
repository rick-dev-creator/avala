using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Simulator.Scenarios;

internal sealed record Scenario(string Name, IReadOnlyList<IReadOnlyList<IStep>> Turns)
{
    public bool Recorded { get; init; }

    public Option<AgentAccount> Account { get; init; }

    public bool AsRecorded { get; init; }

    public IReadOnlyList<IStep> Script(int turn) => Turns[Math.Min(turn, Turns.Count - 1)];
}
