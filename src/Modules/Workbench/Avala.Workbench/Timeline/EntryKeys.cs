using System.Globalization;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Workbench.Timeline;

internal static class EntryKeys
{
    public const string Restart = "restart";

    public static string Attempt(int number) => string.Create(CultureInfo.InvariantCulture, $"attempt:{number}");

    public static string Item(TurnId turn, ItemId item) => $"item:{turn.Value}:{item.Value}";

    public static string Permission(TurnId turn, ItemId item) => $"permission:{turn.Value}:{item.Value}";

    public static string Plan(TurnId turn) => $"plan:{turn.Value}";

    public static string Interjection(TurnId turn, int position) => string.Create(CultureInfo.InvariantCulture, $"interjection:{turn.Value}:{position}");

    public static string TurnEnd(TurnId turn) => $"turn:{turn.Value}";
}
