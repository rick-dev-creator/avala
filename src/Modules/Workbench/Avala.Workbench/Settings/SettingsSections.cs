using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Settings;

internal enum SettingsSection
{
    Accounts,
    Connections,
    Supervision,
    Notifications,
    Appearance,
    About,
    Repository,
}

internal enum RepositoryTab
{
    Permissions,
    Checks,
    Budget,
    Delivery,
    Docs,
    Files,
}

internal interface ISettingsEntryViewModel
{
    SettingsSection Section { get; }

    string Title { get; }

    string Icon { get; }

    bool IsSelected { get; }
}

[INotifyPropertyChanged]
internal sealed partial class SettingsEntryViewModel(SettingsSection section, string title, string icon) : ISettingsEntryViewModel
{
    public SettingsSection Section { get; } = section;

    public string Title { get; } = title;

    public string Icon { get; } = icon;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

internal sealed record SectionHeading(string Title, string Note);

internal static class SectionPhrases
{
    public static IReadOnlyList<SettingsEntryViewModel> Entries() =>
    [
        new(SettingsSection.Accounts, "Accounts", "IconPerson"),
        new(SettingsSection.Connections, "Connections", "IconFlow"),
        new(SettingsSection.Supervision, "Supervision", "IconEye"),
        new(SettingsSection.Notifications, "Notifications", "IconBell"),
        new(SettingsSection.Appearance, "Appearance", "IconAppearance"),
        new(SettingsSection.About, "About", "IconInfo"),
    ];

    public static SectionHeading Heading(SettingsSection section) => section switch
    {
        SettingsSection.Accounts => new("Accounts", "The agents and source control this computer signs in with."),
        SettingsSection.Connections => new("Connections", "Which connection runs a job that names none, and how each harness works."),
        SettingsSection.Supervision => new("Supervision", "When Avala holds a job that went silent, and what jobs may use of this computer."),
        SettingsSection.Notifications => new("Notifications", "What reaches your phone while you are away from this window."),
        SettingsSection.Appearance => new("Appearance", "How Avala looks, moves and reads."),
        SettingsSection.About => new("About", "This build, its diagnostics and what it shares."),
        _ => new("Repositories", "Choose a repository to see the rules its .avala files give every job."),
    };
}
