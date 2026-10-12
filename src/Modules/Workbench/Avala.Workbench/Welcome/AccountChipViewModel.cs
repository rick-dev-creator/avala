using Avala.Workbench.Accounts;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Welcome;

internal interface IAccountChipViewModel
{
    string Label { get; }

    string Monogram { get; }

    string Logo { get; }

    bool HasLogo { get; }

    bool IsAttention { get; }

    string FixLabel { get; }

    IAsyncRelayCommand FixCommand { get; }
}

internal sealed record AccountChipViewModel(string Label, string Monogram, string Logo, bool IsAttention, string FixLabel, IAsyncRelayCommand FixCommand) : IAccountChipViewModel
{
    public bool HasLogo => Logo.Length > 0;

    public static AccountChipViewModel Agent(string name, bool attention, string fix, IAsyncRelayCommand command) =>
        new(name, AccountPhrases.Monogram(name), string.Empty, attention, fix, command);

    public static AccountChipViewModel Forge(string login, string logo, bool attention, string fix, IAsyncRelayCommand command) =>
        new(login, AccountPhrases.Monogram(login), logo, attention, fix, command);
}

internal static class WelcomePhrases
{
    public const string Title = "What should we work on?";

    public const string CheckAgain = "Check again";

    public const string OpenSettings = "Open Settings";

    public const string ConnectionsFile = "connections.json";

    public const string Harness = "Agent";
}
