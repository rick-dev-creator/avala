using Avala.Components.Privacy;
using System.Globalization;
using Avala.Forges.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.RepositoryChoice;

internal interface ICloneSourceViewModel
{
    string Name { get; }

    Personal Login { get; }

    string Logo { get; }

    bool IsSelected { get; }

    IRelayCommand SelectCommand { get; }
}

internal interface IRemoteRowViewModel
{
    string Name { get; }

    Personal Owner { get; }

    bool IsPrivate { get; }

    bool IsArchived { get; }

    string Updated { get; }

    IAsyncRelayCommand CloneCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class CloneSourceViewModel(AccountStatus account, string logo, Action<ForgeName> select) : ICloneSourceViewModel
{
    public ForgeName Forge { get; } = account.Name;

    public string Name { get; } = account.Name.Value;

    public Personal Login { get; } = Accounts.AccountPhrases.Login(account);

    public string Logo { get; } = logo;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [RelayCommand]
    private void Select() => select(Forge);
}

internal sealed class RemoteRowViewModel(RemoteRepository repository, DateTimeOffset now, IAsyncRelayCommand clone) : IRemoteRowViewModel
{
    public RemoteRepository Repository { get; } = repository;

    public string Name { get; } = repository.Address.Name;

    public Personal Owner { get; } = Personal.Login(repository.Address.Owner);

    public bool IsPrivate { get; } = repository.Private;

    public bool IsArchived { get; } = repository.Archived;

    public string Updated { get; } = ClonePhrases.Since(repository.UpdatedAt, now);

    public IAsyncRelayCommand CloneCommand { get; } = clone;
}

internal static class ClonePhrases
{
    public const string Retry = "Retry";

    public const string CheckAgain = "Check again";

    public const string Connect = "Connect";

    public static string Since(DateTimeOffset at, DateTimeOffset now)
    {
        var elapsed = now - at;

        return elapsed.TotalMinutes < 1 ? "now"
            : elapsed.TotalHours < 1 ? $"{(int)elapsed.TotalMinutes}m"
            : elapsed.TotalDays < 1 ? $"{(int)elapsed.TotalHours}h"
            : elapsed.TotalDays < 30 ? $"{(int)elapsed.TotalDays}d"
            : at.Year == now.Year ? at.ToString("MMM d", CultureInfo.InvariantCulture)
            : at.ToString("MMM yyyy", CultureInfo.InvariantCulture);
    }

    public static bool IsAddress(string text)
    {
        var trimmed = text.Trim();

        return trimmed.Length > 0
            && !trimmed.Contains(' ', StringComparison.Ordinal)
            && (trimmed.Contains("://", StringComparison.Ordinal) || trimmed.StartsWith("git@", StringComparison.Ordinal) || trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase));
    }

    public static string Cloning(string name) => $"Cloning {name}…";

    public static string Listing(ForgeError error) => error switch
    {
        ForgeError.MissingCredential => "This account's token is not set.",
        ForgeError.Unauthorized => "The forge refused this account.",
        ForgeError.CliUnavailable => "This account's command-line tool is not installed.",
        ForgeError.Unreachable => "The forge could not be reached.",
        ForgeError.RateLimited => "The forge's rate limit is spent for now.",
        ForgeError.RepositoriesUnsupported => "This forge lists no repositories.",
        _ => "The repositories could not be listed.",
    };

    public static string Fix(ForgeError error) => error switch
    {
        ForgeError.MissingCredential or ForgeError.Unauthorized => Connect,
        _ => CheckAgain,
    };
}
