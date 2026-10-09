using System.Collections.ObjectModel;
using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Submitting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.NewJob;

internal interface INewJobViewModel
{
    string Title { get; }

    IReadOnlyList<string> Repositories { get; }

    IReadOnlyList<string> Connections { get; }

    string Repository { get; set; }

    string Instruction { get; set; }

    string Connection { get; set; }

    string Route { get; }

    bool IsRouteAttention { get; }

    bool Supervised { get; set; }

    string Error { get; }

    string Submitted { get; }

    IAsyncRelayCommand SubmitCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class NewJobViewModel(JobLaunch launch, JobBoard board, IMessenger messenger)
    : INewJobViewModel, IPage, IActivatable, IRecipient<DefaultConnectionChanged>
{
    private readonly ObservableCollection<string> repositories = [];
    private readonly ObservableCollection<string> connections = [NewJobPhrases.Auto];
    private ConnectionCatalog catalog = new(ConnectionFileStatus.Absent, Option<ConnectionError>.None, [], Option<ConnectionName>.None);
    private Option<Result<ConnectionPreview, JobRejection>> preview;
    private int previews;

    public string Title => "New job";

    public PagePlacement Placement => PagePlacement.Hidden;

    public IReadOnlyList<string> Repositories => repositories;

    public IReadOnlyList<string> Connections => connections;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial string Repository { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial string Instruction { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Connection { get; set; } = NewJobPhrases.Auto;

    [ObservableProperty]
    public partial string Route { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsRouteAttention { get; private set; }

    [ObservableProperty]
    public partial bool Supervised { get; set; }

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Submitted { get; private set; } = string.Empty;

    public Option<JobId> LastSubmitted { get; private set; }

    public Task Loading { get; private set; } = Task.CompletedTask;

    public Task Previewing { get; private set; } = Task.CompletedTask;

    public void Activate()
    {
        if (!messenger.IsRegistered<DefaultConnectionChanged>(this))
        {
            messenger.Register(this);
        }

        Loading = LoadAsync(CancellationToken.None);
    }

    public void Deactivate() => messenger.Unregister<DefaultConnectionChanged>(this);

    public void Receive(DefaultConnectionChanged message) => Loading = LoadAsync(CancellationToken.None);

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        repositories.ShowOnly(board.Jobs.Values.OrderByDescending(job => job.Summary.Submitted).Select(job => job.Summary.Repository).Distinct());
        ShowConnections(await launch.CatalogAsync(cancellationToken));

        if (string.IsNullOrWhiteSpace(Repository) && repositories.Count > 0)
        {
            Repository = repositories[0];
        }

        Previewing = PreviewAsync(cancellationToken);
        await Previewing;
    }

    partial void OnRepositoryChanged(string value) => Previewing = PreviewAsync(CancellationToken.None);

    partial void OnConnectionChanged(string value) => ShowRoute();

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync(CancellationToken cancellationToken)
    {
        var submitted = await launch.SubmitAsync(Repository, Instruction, Chosen(), Supervised, cancellationToken);
        Error = submitted.Match(_ => string.Empty, NewJobPhrases.Rejection);

        if (submitted.TryGetValue(out var job, out _))
        {
            LastSubmitted = job;
            Submitted = $"Submitted: {Instruction.Trim()}";
            Instruction = string.Empty;
        }
    }

    private bool CanSubmit() => !string.IsNullOrWhiteSpace(Repository) && !string.IsNullOrWhiteSpace(Instruction);

    private void ShowConnections(ConnectionCatalog shown)
    {
        var chosen = Chosen();
        catalog = shown;
        var following = NewJobPhrases.Following(shown);
        string[] named = [.. shown.Connections.Select(connection => connection.Name.Value)];
        connections.ShowOnly([following, .. named]);
        Connection = chosen.Match(name => named.Contains(name.Value, StringComparer.Ordinal) ? name.Value : following, () => following);
    }

    private Option<ConnectionName> Chosen() =>
        string.IsNullOrEmpty(Connection) || connections.Count == 0 || Connection == connections[0]
            ? Option<ConnectionName>.None
            : new ConnectionName(Connection);

    private async Task PreviewAsync(CancellationToken cancellationToken)
    {
        var ticket = ++previews;
        var previewed = await launch.PreviewAsync(Repository, cancellationToken);

        if (ticket == previews)
        {
            preview = previewed;
            ShowRoute();
        }
    }

    private void ShowRoute()
    {
        var (text, attention) = NewJobPhrases.Route(catalog, preview, Chosen());
        Route = text;
        IsRouteAttention = attention;
    }
}
