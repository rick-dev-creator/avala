using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Accounts;
using Avala.Workbench.NewJob;
using Avala.Workbench.Submitting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Onboarding;

internal enum AgentState
{
    Ready,
    AtLimit,
    SignedOut,
}

internal interface IAgentRowViewModel
{
    string Name { get; }

    string Monogram { get; }

    string Provider { get; }

    AgentState State { get; }

    string Status { get; }
}

internal sealed record AgentRowViewModel(string Name, string Monogram, string Provider, AgentState State, string Status) : IAgentRowViewModel
{
    public static AgentRowViewModel Of(DeclaredConnection connection, Option<ConnectionError> problem, ConnectionCatalog catalog, Option<Result<ConnectionPreview, JobRejection>> preview)
    {
        var state = problem.IsSome ? AgentState.SignedOut
            : NewJobPhrases.Compared(preview, connection.Name).Match(candidate => !candidate.Available, () => false) ? AgentState.AtLimit
            : AgentState.Ready;

        return new(
            connection.Name.Value,
            AccountPhrases.Monogram(connection.Name.Value),
            catalog.Providers.FirstOrDefault(provider => provider.Id == connection.Provider)?.Name ?? connection.Provider,
            state,
            AgentPhrases.Status(state, problem));
    }
}

internal interface IInstallRowViewModel
{
    string Provider { get; }

    string Command { get; }

    string SignIn { get; }

    bool IsCopied { get; }

    IAsyncRelayCommand CopyCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class InstallRowViewModel(string provider, string command, string signIn, IClipboard clipboard) : IInstallRowViewModel
{
    public string Provider => provider;

    public string Command => command;

    public string SignIn => signIn;

    [ObservableProperty]
    public partial bool IsCopied { get; private set; }

    [RelayCommand]
    private async Task CopyAsync(CancellationToken cancellationToken)
    {
        IsCopied = false;
        IsCopied = await clipboard.CopyAsync(Command, cancellationToken);
    }
}

internal interface IAgentsStepViewModel : IOnboardingStep
{
    IReadOnlyList<IAgentRowViewModel> Agents { get; }

    IReadOnlyList<IInstallRowViewModel> Installs { get; }

    bool HasAgents { get; }

    IAsyncRelayCommand CheckAgainCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class AgentsStepViewModel(JobLaunch launch, IClipboard clipboard) : IAgentsStepViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAgents))]
    public partial IReadOnlyList<IAgentRowViewModel> Agents { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<IInstallRowViewModel> Installs { get; private set; } = [];

    public bool HasAgents => Agents.Count > 0;

    public async Task LoadAsync(CancellationToken cancellationToken) => await ShowAsync(await launch.CatalogAsync(cancellationToken), cancellationToken);

    [RelayCommand]
    private async Task CheckAgainAsync(CancellationToken cancellationToken) => await ShowAsync(await launch.RediscoverAsync(cancellationToken), cancellationToken);

    private async Task ShowAsync(ConnectionCatalog catalog, CancellationToken cancellationToken)
    {
        var preview = await launch.PreviewAsync(string.Empty, cancellationToken);
        var rows = new List<IAgentRowViewModel>();

        foreach (var connection in catalog.Connections)
        {
            var problem = connection.Problem.IsSome ? connection.Problem : await launch.ProblemAsync(connection.Name, cancellationToken);
            rows.Add(AgentRowViewModel.Of(connection, problem, catalog, preview));
        }

        Agents = rows;
        Installs = [.. catalog.Providers
            .Where(provider => provider.Install.IsSome)
            .Select(provider => new InstallRowViewModel(
                provider.Name,
                provider.Install.Match(command => command, () => string.Empty),
                AgentPhrases.SignIn(provider.SignIn),
                clipboard))];
    }
}

internal static class AgentPhrases
{
    public static string SignIn(Option<string> command) =>
        command.Match(found => $"Then run {found} once to sign in.", () => "Then sign in once.");

    public static string Status(AgentState state, Option<ConnectionError> problem) => state switch
    {
        AgentState.Ready => "Signed in",
        AgentState.AtLimit => "At its limit",
        _ => problem.Match(
            error => error switch
            {
                ConnectionError.MissingFolder => "Signed out",
                ConnectionError.MissingVariable => "Key not set",
                _ => "Not usable",
            },
            () => "Not usable"),
    };
}
