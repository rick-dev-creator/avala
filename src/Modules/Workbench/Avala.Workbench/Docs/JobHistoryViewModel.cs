using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Docs;

internal interface IJobHistoryViewModel
{
    bool HasSteps { get; }

    IReadOnlyList<IHistoryStepViewModel> Steps { get; }

    string Heading { get; }

    string Happened { get; }

    string Why { get; }

    IReadOnlyList<string> Changed { get; }

    string Records { get; }

    bool HasWriter { get; }

    IRelayCommand OpenWriterCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class JobHistoryViewModel : IJobHistoryViewModel
{
    private readonly ObservableCollection<HistoryStepViewModel> steps = [];
    private JobDocs? shown;
    private Option<JobId> writer;

    public event EventHandler<int>? VersionChosen;

    public event EventHandler<JobId>? WriterOpened;

    public IReadOnlyList<IHistoryStepViewModel> Steps => steps;

    public IReadOnlyList<HistoryStepViewModel> Listed => steps;

    [ObservableProperty]
    public partial bool HasSteps { get; private set; }

    [ObservableProperty]
    public partial string Heading { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Happened { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Why { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Changed { get; private set; } = [];

    [ObservableProperty]
    public partial string Records { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenWriterCommand))]
    public partial bool HasWriter { get; private set; }

    public void Show(JobDocs docs)
    {
        shown = docs;
        var versions = docs.Documentation.Versions;
        var chosen = steps.FirstOrDefault(step => step.IsChosen)?.Number ?? (versions.Count > 0 ? versions[^1].Number : 0);
        steps.Clear();

        foreach (var version in docs.Documentation.Versions)
        {
            foreach (var request in docs.Documentation.Requests.Where(request => request.Version.Match(number => number == version.Number, () => false)))
            {
                steps.Add(HistoryStepViewModel.Request(request));
            }

            steps.Add(HistoryStepViewModel.Version(version, Choose));
        }

        foreach (var request in docs.Documentation.Requests.Where(request => request.Version.IsNone))
        {
            steps.Add(HistoryStepViewModel.Request(request));
        }

        HasSteps = steps.Count > 0;
        Summarize(chosen);
    }

    [RelayCommand(CanExecute = nameof(HasWriter))]
    private void OpenWriter()
    {
        foreach (var found in writer.Match<JobId[]>(job => [job], () => []))
        {
            WriterOpened?.Invoke(this, found);
        }
    }

    public void Clear()
    {
        shown = null;
        steps.Clear();
        HasSteps = false;
        Heading = string.Empty;
        Happened = string.Empty;
        Why = string.Empty;
        Changed = [];
        Records = string.Empty;
        writer = Option<JobId>.None;
        HasWriter = false;
    }

    private void Choose(int number)
    {
        Summarize(number);
        VersionChosen?.Invoke(this, number);
    }

    private void Summarize(int number)
    {
        foreach (var step in steps)
        {
            step.Mark(step.IsVersion && step.Number == number);
        }

        if (shown is not { } docs || docs.Documentation.Versions.FirstOrDefault(version => version.Number == number) is not { } version)
        {
            return;
        }

        var played = docs.Story.Match(story => story.Versions.FirstOrDefault(found => found.Number == number), () => null);
        var asked = docs.Documentation.Requests.Where(request => request.Version.Match(produced => produced == number, () => false)).ToList();
        Heading = HistoryPhrases.Heading(version);
        Happened = played?.Document.Lead ?? string.Empty;
        Why = HistoryPhrases.Why(version.Kind, asked);
        Changed = played is null ? [] : [.. played.Steps.Where(step => step.Kind is StepKind.Added or StepKind.Changed or StepKind.Removed).Select(HistoryPhrases.Change)];
        Records = string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileName(version.Path)} · plans/v{version.Number}.md");
        writer = version.Writer;
        HasWriter = writer.IsSome;
    }
}

internal interface IHistoryStepViewModel
{
    string Title { get; }

    string Fact { get; }

    bool IsVersion { get; }

    bool IsChosen { get; }

    IRelayCommand ChooseCommand { get; }
}

internal sealed record HistoryStep(int Number, string Title, string Fact, bool IsVersion);

[INotifyPropertyChanged]
internal sealed partial class HistoryStepViewModel(HistoryStep step, Action<int> choose) : IHistoryStepViewModel
{
    public int Number { get; } = step.Number;

    public string Title { get; } = step.Title;

    public string Fact { get; } = step.Fact;

    public bool IsVersion { get; } = step.IsVersion;

    [ObservableProperty]
    public partial bool IsChosen { get; private set; }

    public static HistoryStepViewModel Version(FeatureVersion version, Action<int> choose) =>
        new(new HistoryStep(version.Number, HistoryPhrases.Title(version), version.At.ToLocalTime().ToString("MMM d, HH:mm", CultureInfo.InvariantCulture), true), choose);

    public static HistoryStepViewModel Request(ChangeRequest request) =>
        new(new HistoryStep(0, string.Create(CultureInfo.InvariantCulture, $"Change request #{request.Number}"), $"“{request.Text}”", false), _ => { });

    public void Mark(bool chosen) => IsChosen = chosen;

    [RelayCommand(CanExecute = nameof(IsVersion))]
    private void Choose() => choose(Number);
}

internal static class HistoryPhrases
{
    public static string Title(FeatureVersion version) => version.Kind switch
    {
        VersionKind.Proposed => string.Create(CultureInfo.InvariantCulture, $"Plan v{version.Number} · proposed"),
        VersionKind.Deviation => string.Create(CultureInfo.InvariantCulture, $"Plan v{version.Number} · deviation"),
        _ => string.Create(CultureInfo.InvariantCulture, $"v{version.Number} · as built"),
    };

    public static string Heading(FeatureVersion version) => Title(version);

    public static string Why(VersionKind kind, IReadOnlyList<ChangeRequest> asked) =>
        asked.Count > 0 ? string.Join(" ", asked.Select(request => $"You asked “{request.Text.TrimEnd('.', '!', '?')}”.")) + " The plan changed to do it."
        : kind switch
        {
            VersionKind.Proposed => "None. It is the plan the agent proposed.",
            VersionKind.Deviation => "The agent changed its plan while it worked.",
            _ => "None. It records what was built.",
        };

    public static string Change(PlayStep step) => step.Kind switch
    {
        StepKind.Added => $"+ {step.Title}",
        StepKind.Changed => $"~ {step.Title}",
        _ => $"− {step.Title}",
    };
}
