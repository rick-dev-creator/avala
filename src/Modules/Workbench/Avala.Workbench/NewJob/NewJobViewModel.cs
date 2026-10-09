using System.Collections.ObjectModel;
using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Presenting;
using Avala.Workbench.Submitting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.NewJob;

[INotifyPropertyChanged]
internal sealed partial class NewJobViewModel(JobLaunch launch, JobBoard board) : IPage, IActivatable
{
    public const string RepositoryDefault = "The repository's default";

    public string Title => "New job";

    public ObservableCollection<string> Repositories { get; } = [];

    public ObservableCollection<string> Connections { get; } = [RepositoryDefault];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial string Repository { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial string Instruction { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Connection { get; set; } = RepositoryDefault;

    [ObservableProperty]
    public partial bool Supervised { get; set; }

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Submitted { get; private set; } = string.Empty;

    public Option<JobId> LastSubmitted { get; private set; }

    public Task Loading { get; private set; } = Task.CompletedTask;

    public void Activate() => Loading = LoadAsync(CancellationToken.None);

    public void Deactivate()
    {
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        Repositories.ShowOnly(board.Jobs.Values.OrderByDescending(job => job.Summary.Submitted).Select(job => job.Summary.Repository).Distinct());
        var chosen = Connection;
        string[] connections = [RepositoryDefault, .. (await launch.ConnectionsAsync(cancellationToken)).Select(name => name.Value)];
        Connections.ShowOnly(connections);
        Connection = Connections.Contains(chosen) ? chosen : RepositoryDefault;

        if (string.IsNullOrWhiteSpace(Repository) && Repositories.Count > 0)
        {
            Repository = Repositories[0];
        }
    }

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync(CancellationToken cancellationToken)
    {
        var connection = string.IsNullOrEmpty(Connection) || Connection == RepositoryDefault
            ? Option<ConnectionName>.None
            : new ConnectionName(Connection);
        var submitted = await launch.SubmitAsync(Repository, Instruction, connection, Supervised, cancellationToken);
        Error = submitted.Match(_ => string.Empty, NewJobPhrases.Rejection);

        if (submitted.TryGetValue(out var job, out _))
        {
            LastSubmitted = job;
            Submitted = $"Submitted: {Instruction.Trim()}";
            Instruction = string.Empty;
        }
    }

    private bool CanSubmit() => !string.IsNullOrWhiteSpace(Repository) && !string.IsNullOrWhiteSpace(Instruction);
}

internal static class NewJobPhrases
{
    public static string Rejection(JobRejection rejection) => rejection switch
    {
        JobRejection.EmptyRepository => "Name the repository the job works in.",
        JobRejection.EmptyInstruction => "Write what the agent should do.",
        JobRejection.UnknownConnection => "No connection has that name.",
        JobRejection.UnusableConnection => "That connection cannot be used: its credential or the repository's jobs.json is not usable.",
        JobRejection.WorkspaceUnavailable => "The repository's worktree could not be prepared.",
        JobRejection.AgentUnavailable => "The agent could not start.",
        JobRejection.InvalidRequest => "The request is invalid.",
        JobRejection.InvalidAttemptBudget => "The number of attempts is invalid.",
        _ => "The job was refused.",
    };
}
