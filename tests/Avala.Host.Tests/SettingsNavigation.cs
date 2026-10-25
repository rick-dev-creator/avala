namespace Avala.Host.Tests;

internal static class SettingsNavigation
{
    public static void ShowSection(this Bound page, string section) =>
        page.Execute("ShowCommand", Parsed(page, "SettingsSection", section));

    public static void ShowTab(this Bound page, string tab) =>
        page["Repository"].Execute("ShowTabCommand", Parsed(page, "RepositoryTab", tab));

    public static Task OpenRepositoryAsync(this Bound page, string repository) =>
        ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand<string>)page["OpenRepositoryCommand"].Target).ExecuteAsync(repository);

    private static object Parsed(Bound page, string type, string value) =>
        Enum.Parse(page.Target.GetType().Assembly.GetType("Avala.Workbench.Settings." + type)!, value);
}
