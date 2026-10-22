using System.Globalization;
using Avala.Agents.Contracts.Sessions;

namespace Avala.Workbench.Timeline;

internal static class EntryKeys
{
    public const string Restart = "restart";

    public static string EarlierRestart(int number) => string.Create(CultureInfo.InvariantCulture, $"restart:{number}");

    public static string Attempt(int number) => string.Create(CultureInfo.InvariantCulture, $"attempt:{number}");

    public static string Item(TurnId turn, ItemId item) => $"item:{turn.Value}:{item.Value}";

    public static string Permission(TurnId turn, ItemId item) => $"permission:{turn.Value}:{item.Value}";

    public static string Denied(TurnId turn, ItemId item) => $"denied:{turn.Value}:{item.Value}";

    public static string Plan(TurnId turn) => $"plan:{turn.Value}";

    public static string Retry(TurnId turn) => $"retry:{turn.Value}";

    public static string Interjection(TurnId turn, int position) => string.Create(CultureInfo.InvariantCulture, $"interjection:{turn.Value}:{position}");

    public static string TurnEnd(TurnId turn) => $"turn:{turn.Value}";

    public static string Ci(int pullRequest, string head) => string.Create(CultureInfo.InvariantCulture, $"ci:{pullRequest}:{head}");

    public static string Docs(int version) => string.Create(CultureInfo.InvariantCulture, $"docs:{version}");

    public static string Unwritten(int index) => string.Create(CultureInfo.InvariantCulture, $"docs-unwritten:{index}");

    public static string Change(int request) => string.Create(CultureInfo.InvariantCulture, $"change:{request}");

    public static string ChildAsking(SessionId session, ItemId item) => $"child-asking:{session.Value}:{item.Value}";
}
