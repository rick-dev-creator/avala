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
using Avala.Workbench.RepositoryChoice;
using Avala.Workbench.Submitting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.NewJob;

internal interface INewJobViewModel
{
    string Title { get; }

    IRepositoryFieldViewModel RepositoryField { get; }

    IReadOnlyList<string> Connections { get; }

    IReadOnlyList<IConnectionOptionViewModel> Options { get; }

    string Instruction { get; set; }

    string Connection { get; set; }

    string Route { get; }

    bool IsRouteAttention { get; }

    IReadOnlyList<string> Autonomies { get; }

    string Autonomy { get; set; }

    string AutonomyNote { get; }

    IJobOptionsViewModel JobOptions { get; }

    string Error { get; }

    string Submitted { get; }

    IAsyncRelayCommand SubmitCommand { get; }

    IAsyncRelayCommand StartWithoutRepositoryCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class NewJobViewModel
    : INewJobViewModel, IPage, IActivatable, IRecipient<DefaultConnectionChanged>
{
    private readonly JobLaunch launch;
    private readonly NewJobReadings readings;
    private readonly IMessenger messenger;
    private readonly JobOptionsViewModel choices;
    private readonly RepositoryFieldViewModel chooser;
    private readonly ObservableCollection<string> connections = [NewJobPhrases.Auto];
    private readonly ObservableCollection<ConnectionOptionViewModel> options = [new(NewJobPhrases.Auto)];
    private readonly ObservableCollection<string> autonomies = [NewJobPhrases.RepositoryLevel(Option<RepositoryPolicy>.None)];
    private ConnectionCatalog catalog = new(ConnectionFileStatus.Absent, Option<ConnectionError>.None, [], Option<ConnectionName>.None);
    private Option<Result<ConnectionPreview, JobRejection>> preview;
    private Option<RepositoryPolicy> policy;
    private int previews;
    private int offers;

    public NewJobViewModel(JobLaunch launch, NewJobReadings readings, IMessenger messenger, NewJobFields fields)
    {
        this.launch = launch;
        this.readings = readings;
        this.messenger = messenger;
        choices = fields.Options;
        chooser = fields.Repository;
        chooser.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(RepositoryFieldViewModel.Repository))
            {
                SubmitCommand.NotifyCanExecuteChanged();
                Previewing = PreviewAsync(CancellationToken.None);
            }
        };
    }

    public string Title => "New job";

    public PagePlacement Placement => PagePlacement.Hidden;

    public IRepositoryFieldViewModel RepositoryField => chooser;

    public RepositoryFieldViewModel Chooser => chooser;

    public string Repository
    {
        get => chooser.Repository;
        set => chooser.Repository = value;
    }

    public IReadOnlyList<string> Connections => connections;

    public IReadOnlyList<IConnectionOptionViewModel> Options => options;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartWithoutRepositoryCommand))]
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

    public IJobOptionsViewModel JobOptions => choices;

    public IModelPickerViewModel Models => choices.Models;

    public ModelPickerViewModel Picker => choices.Picker;

    public Task OptionsRead { get; private set; } = Task.CompletedTask;

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
        chooser.ShowKnown();
        ShowConnections(await launch.CatalogAsync(cancellationToken));

        if (string.IsNullOrWhiteSpace(Repository) && chooser.Repositories.Count > 0)
        {
            Repository = chooser.Repositories[0];
        }

        Previewing = PreviewAsync(cancellationToken);
        await Previewing;
    }

    partial void OnConnectionChanged(string value)
    {
        ShowRoute();
        Offering = OfferAsync(CancellationToken.None);
    }

    partial void OnAutonomyChanged(string value) => AutonomyNote = NewJobPhrases.AutonomyNote(policy, Supervised);

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private Task SubmitAsync(CancellationToken cancellationToken) => StartAsync(Repository, string.Empty, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanStartWithoutRepository))]
    private async Task StartWithoutRepositoryAsync(CancellationToken cancellationToken)
    {
        var made = await launch.ScratchAsync(Instruction, cancellationToken);

        if (!made.TryGetValue(out var scratch, out var failure))
        {
            Error = NewJobPhrases.NoScratch(failure);

            return;
        }

        await StartAsync(scratch, NewJobPhrases.InScratch, cancellationToken);
    }

    private async Task StartAsync(string repository, string where, CancellationToken cancellationToken)
    {
        if (choices.IsContest)
        {
            await StartContestAsync(repository, where, cancellationToken);

            return;
        }

        var submitted = await launch.SubmitAsync(repository, Instruction, Chosen(), Supervised, choices.Picker.Chosen, cancellationToken);
        Error = submitted.Match(_ => string.Empty, NewJobPhrases.Rejection);

        if (submitted.TryGetValue(out var job, out _))
        {
            Started(job, $"Submitted{where}: {Instruction.Trim()}");
        }
    }

    private async Task StartContestAsync(string repository, string where, CancellationToken cancellationToken)
    {
        var request = JobLaunch.Request(repository, Instruction, Chosen(), Supervised, choices.Picker.Chosen);
        var started = await launch.StartContestAsync(request, choices.Candidates, choices.Spread, cancellationToken);
        Error = started.Match(_ => string.Empty, rejection => $"The contest did not start: {ContestPhrases.Rejection(rejection)}");

        if (started.TryGetValue(out var contest, out _))
        {
            Started(contest.Candidates[0].Job, $"Started {ContestPhrases.Candidates(contest.Candidates.Count)}{where}: {Instruction.Trim()}");
        }
    }

    private void Started(JobId job, string submitted)
    {
        LastSubmitted = job;
        Submitted = submitted;
        Instruction = string.Empty;
    }

    private bool CanSubmit() => !string.IsNullOrWhiteSpace(Repository) && !string.IsNullOrWhiteSpace(Instruction);

    private bool CanStartWithoutRepository() => !string.IsNullOrWhiteSpace(Instruction);

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
            OptionsRead = choices.ReadAsync(Repository, cancellationToken);
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
                () => () => choices.Picker.Hide(ModelPhrases.Unoffered(target, following)))();
        }
    }

    private void ShowModels(OffersModels offered, ModelChoice repository, bool following, Option<ConnectionName> target)
    {
        if (following)
        {
            choices.Picker.Follow(ModelPhrases.Job(offered, repository), offered.Efforts.Count > 0, target.Match(name => ModelPhrases.Following(name, offered, repository), () => string.Empty));
        }
        else
        {
            choices.Picker.Offer(offered, ModelPhrases.Job(offered, repository), ModelChoice.Default, ModelPhrases.JobNote(repository));
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
        var (text, attention) = NewJobPhrases.Route(catalog, preview, Chosen());
        Route = text;
        IsRouteAttention = attention;
        ShowReadings();
    }
}
