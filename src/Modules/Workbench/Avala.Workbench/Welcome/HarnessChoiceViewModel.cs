using System.Collections.ObjectModel;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Accounts;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.NewJob;
using Avala.Workbench.Presenting;
using Avala.Workbench.Submitting;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Welcome;

internal interface IHarnessChoiceViewModel
{
    bool IsShown { get; }

    string Label { get; }

    string Monogram { get; }

    string Reason { get; }

    bool IsAttention { get; }

    bool IsOpen { get; set; }

    IReadOnlyList<IHarnessOptionViewModel> Options { get; }

    IModelPickerViewModel Models { get; }
}

[INotifyPropertyChanged]
internal sealed partial class HarnessChoiceViewModel : IHarnessChoiceViewModel
{
    private readonly JobLaunch launch;
    private readonly NewJobReadings readings;
    private readonly ModelPickerViewModel models;
    private readonly ObservableCollection<HarnessOptionViewModel> options = [];
    private ConnectionCatalog catalog = new(ConnectionFileStatus.Absent, Option<ConnectionError>.None, [], Option<ConnectionName>.None);
    private Option<Result<ConnectionPreview, JobRejection>> preview;
    private int shows;
    private int offers;

    public HarnessChoiceViewModel(JobLaunch launch, NewJobReadings readings, ModelPickerViewModel models)
    {
        this.launch = launch;
        this.readings = readings;
        this.models = models;
        models.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName is nameof(ModelPickerViewModel.Model) or nameof(ModelPickerViewModel.Effort))
            {
                ShowLabel();
            }
        };
    }

    public IReadOnlyList<IHarnessOptionViewModel> Options => options;

    public IModelPickerViewModel Models => models;

    public Option<ConnectionName> Chosen { get; private set; }

    public ModelChoice Model => models.Chosen;

    public Task Offering { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial string Label { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Monogram { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Reason { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAttention { get; private set; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    public async Task ShowAsync(ConnectionCatalog shown, CancellationToken cancellationToken)
    {
        var ticket = ++shows;
        var previewed = await launch.PreviewAsync(string.Empty, cancellationToken);

        if (ticket != shows)
        {
            return;
        }

        catalog = shown;
        preview = previewed;
        var usable = shown.Error.IsNone ? shown.Connections.Where(connection => connection.Problem.IsNone).Select(connection => connection.Name).ToList() : [];
        Chosen = Chosen.Bind(name => usable.Contains(name) ? name : Option<ConnectionName>.None);
        options.ShowOnly([new HarnessOptionViewModel(HarnessPhrases.Following(shown), Option<ConnectionName>.None, Choose), .. usable.Select(name => new HarnessOptionViewModel(name.Value, name, Choose))]);
        IsShown = usable.Count > 0;
        Mark();
        Offering = OfferAsync(cancellationToken);
        await Offering;
    }

    private void Choose(Option<ConnectionName> connection)
    {
        Chosen = connection;
        Mark();
        Offering = OfferAsync(CancellationToken.None);
    }

    private void Mark()
    {
        foreach (var option in options)
        {
            var name = option.Connection.IsSome ? option.Connection : Following();
            option.Show(
                option.Connection.Match(found => readings.Reading(preview, found), () => HarnessPhrases.Recommended(name)),
                AtLimit(name),
                option.Connection == Chosen);
        }

        ShowLabel();
    }

    private void ShowLabel()
    {
        var running = Chosen.IsSome ? Chosen : Following();
        var (reason, attention) = Chosen.Match(
            name => (readings.Reading(preview, name), AtLimit(name)),
            () => HarnessPhrases.Why(catalog, preview));
        Label = HarnessPhrases.Label(Chosen.IsSome ? Option<string>.None : Option<string>.Some(HarnessPhrases.Following(catalog)), running, models);
        Monogram = running.Match(name => AccountPhrases.Monogram(name.Value), () => string.Empty);
        Reason = reason;
        IsAttention = attention;
    }

    private bool AtLimit(Option<ConnectionName> connection) =>
        connection.Bind(name => NewJobPhrases.Compared(preview, name)).Match(candidate => !candidate.Available, () => false);

    private Option<ConnectionName> Following() =>
        catalog.DefaultMode == DefaultMode.Fixed
            ? catalog.Default
            : preview.Bind(previewed => previewed.Match(found => found.Connection, _ => Option<ConnectionName>.None));

    private async Task OfferAsync(CancellationToken cancellationToken)
    {
        var ticket = ++offers;
        var offered = await Chosen.Match(
            name => launch.OfferAsync(name, cancellationToken).AsTask(),
            () => Task.FromResult(Option<OffersModels>.None));

        if (ticket == offers)
        {
            offered.Match<Action>(
                found => () => models.Offer(found, ModelPhrases.Job(found, ModelChoice.Default), ModelChoice.Default, string.Empty),
                () => () => models.Hide(string.Empty))();
            ShowLabel();
        }
    }
}
