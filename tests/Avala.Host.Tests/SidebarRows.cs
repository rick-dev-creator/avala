namespace Avala.Host.Tests;

internal static class SidebarRows
{
    public static IEnumerable<Bound> TopLevel(Bound sidebar) =>
        sidebar["NeedsYou"].Items.Concat(sidebar["Projects"].Items.Append(sidebar["Chats"]).SelectMany(group => group["Open"].Items.Concat(group["Done"].Items)));

    public static IEnumerable<Bound> In(Bound sidebar, string group) =>
        TopLevel(sidebar).Where(row => row["Group"].Text == group);

    public static string Describe(Bound sidebar) =>
        string.Join(", ", TopLevel(sidebar).Select(row => $"{row["Group"].Text}: {row["Fact"].Text}"));
}
