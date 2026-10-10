using System.Collections.ObjectModel;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Contracts.Presentation;
using Avala.Workbench.ModelChoices;
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

    IReadOnlyList<IConnectionOptionViewModel> Options { get; }

    string Repository { get; set; }

    string Instruction { get; set; }

    string Connection { get; set; }

    string Route { get; }

    bool IsRouteAttention { get; }

    IReadOnlyList<string> Autonomies { get; }

    string Autonomy { get; set; }

    string AutonomyNote { get; }

    IModelPickerViewModel Models { get; }

    string Error { get; }

    string Submitted { get; }

    IAsyncRelayCommand SubmitCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class NewJobViewModel(JobLaunch launch, NewJobReadings readings, IMessenger messenger, ModelPickerViewModel models)
    : INewJobViewModel, IPage, IActivatable, IRecipient<DefaultConnectionChanged>
{
    private readonly ObservableCollection<string> repositories = [];
    private readonly ObservableCollection<string> connections = [NewJobPhrases.Auto];
    private readonly ObservableCollection<ConnectionOptionViewModel> options = [new(NewJobPhrases.Auto)];
    private readonly ObservableCollection<string> autonomies = [NewJobPhrases.RepositoryLevel(Option<RepositoryPolicy>.None)];
    private ConnectionCatalog catalog = new(ConnectionFileStatus.Absent, Option<ConnectionError>.None, [], Option<ConnectionName>.None);
    private Option<Result<ConnectionPreview, JobRejection>> preview;
    private Option<RepositoryPolicy> policy;
    private int previews;
    private int offers;

    public string Title => "New job";

    public PagePlacement Placement => PagePlacement.Hidden;

    public IReadOnlyList<string> Repositories => repositories;

    public IReadOnlyList<string> Connections => connections;

    public IReadOnlyList<IConnectionOptionViewModel> Options => options;

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

    public IReadOnlyList<string> Autonomies => autonomies;

    [ObservableProperty]
    public partial string Autonomy { get; set; } = NewJobPhrases.RepositoryLevel(Option<RepositoryPolicy>.None);

    [ObservableProperty]
    public partial string AutonomyNote { get; private set; } = NewJobPhrases.AutonomyNote(Option<RepositoryPolicy>.None, supervised: false);

    public bool Supervised => Autonomy == NewJobPhrases.Supervised;

    public IModelPickerViewModel Models => models;

    public ModelPickerViewModel Picker => models;

    public Task Offering { get; private set; } = Task.CompletedTask;

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
        repositories.ShowOnly(readings.Repositories());
        ShowConnections(await launch.CatalogAsync(cancellationToken));

        if (string.IsNullOrWhiteSpace(Repository) && repositories.Count > 0)
        {
            Repository = repositories[0];
        }

        Previewing = PreviewAsync(cancellationToken);
        await Previewing;
    }

    partial void OnRepositoryChanged(string value) => Previewing = PreviewAsync(CancellationToken.None);

    partial void OnConnectionChanged(string value)
    {
        ShowRoute();
        Offering = OfferAsync(CancellationToken.None);
    }

    partial void OnAutonomyChanged(string value) => AutonomyNote = NewJobPhrases.AutonomyNote(policy, Supervised);

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync(CancellationToken cancellationToken)
    {
        var submitted = await launch.SubmitAsync(Repository, Instruction, Chosen(), Supervised, models.Chosen, cancellationToken);
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
        options.Reconcile(connections, option => option.Name, name => name, name => new ConnectionOptionViewModel(name), (_, _) => { });
        Connection = chosen.Match(name => named.Contains(name.Value, StringComparer.Ordinal) ? name.Value : following, () => following);
        ShowReadings();
    }

    private void ShowReadings()
    {
        var target = catalog.DefaultMode == DefaultMode.Fixed ? catalog.Default : Option<ConnectionName>.None;

        foreach (var option in options)
        {
            var name = option == options[0] ? target : new ConnectionName(option.Name);
            var compared = name.Bind(found => NewJobPhrases.Compared(preview, found));
            option.Show(
                name.Match(Reading, () => string.Empty),
                compared.Match(candidate => !candidate.Available, () => false));
        }
    }

    private string Reading(ConnectionName name) => readings.Reading(preview, name);

    private Option<ConnectionName> Chosen() =>
        string.IsNullOrEmpty(Connection) || connections.Count == 0 || Connection == connections[0]
            ? Option<ConnectionName>.None
            : new ConnectionName(Connection);

    private async Task PreviewAsync(CancellationToken cancellationToken)
    {
        var ticket = ++previews;
        var previewed = await launch.PreviewAsync(Repository, cancellationToken);
        var declared = await launch.PolicyAsync(Repository, cancellationToken);

        if (ticket == previews)
        {
            preview = previewed;
            ShowRoute();
            ShowAutonomy(declared);
            Offering = OfferAsync(cancellationToken);
            await Offering;
        }
    }

    private async Task OfferAsync(CancellationToken cancellationToken)
    {
        var ticket = ++offers;
        var repository = preview.Match(previewed => previewed.Match(found => found.Model, _ => ModelChoice.Default), () => ModelChoice.Default);
        var following = Chosen().IsNone && catalog.DefaultMode == DefaultMode.Auto;
        var target = Chosen().IsSome ? Chosen()
            : !following ? catalog.Default
            : preview.Bind(previewed => previewed.Match(found => found.Connection, _ => Option<ConnectionName>.None));
        var offered = await target.Match(
            name => launch.OfferAsync(name, cancellationToken).AsTask(),
            () => Task.FromResult(Option<OffersModels>.None));

        if (ticket == offers)
        {
            offered.Match<Action>(
                found => () => ShowModels(found, repository, following, target),
                () => () => models.Hide(ModelPhrases.Unoffered(target, following)))();
        }
    }

    private void ShowModels(OffersModels offered, ModelChoice repository, bool following, Option<ConnectionName> target)
    {
        if (following)
        {
            models.Follow(ModelPhrases.Job(offered, repository), offered.Efforts.Count > 0, target.Match(name => ModelPhrases.Following(name, offered, repository), () => string.Empty));
        }
        else
        {
            models.Offer(offered, ModelPhrases.Job(offered, repository), ModelChoice.Default, ModelPhrases.JobNote(repository));
        }
    }

    private void ShowAutonomy(Option<RepositoryPolicy> declared)
    {
        var supervised = Supervised;
        policy = declared;
        var level = NewJobPhrases.RepositoryLevel(declared);
        var tightens = declared.Match(found => found.Autonomy != Jobs.Contracts.Autonomy.Supervised, () => true);
        autonomies.ShowOnly(tightens ? [level, NewJobPhrases.Supervised] : [level]);
        Autonomy = supervised && tightens ? NewJobPhrases.Supervised : level;
        AutonomyNote = NewJobPhrases.AutonomyNote(policy, Supervised);
    }

    private void ShowRoute()
    {
        var (text, attention) = NewJobPhrases.Route(catalog, preview, Chosen(), Reading);
        Route = text;
        IsRouteAttention = attention;
        ShowReadings();
    }
}
