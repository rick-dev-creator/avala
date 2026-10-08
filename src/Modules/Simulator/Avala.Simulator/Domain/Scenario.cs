namespace Avala.Simulator.Domain;

internal sealed record Scenario(string Name, IReadOnlyList<IReadOnlyList<IStep>> Turns)
{
    public IReadOnlyList<IStep> Script(int turn) => Turns[Math.Min(turn, Turns.Count - 1)];
}
