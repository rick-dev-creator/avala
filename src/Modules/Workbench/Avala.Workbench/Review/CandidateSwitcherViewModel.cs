using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Review;

internal interface ICandidateSwitcherViewModel
{
    bool IsShown { get; }

    string Heading { get; }

    IReadOnlyList<CandidateChoice> Choices { get; }

    IRelayCommand<object> SwitchCommand { get; }
}

internal sealed record CandidateChoice(JobId Job, string Title, string Facts, bool IsOpen, bool IsWinner);

[INotifyPropertyChanged]
internal sealed partial class CandidateSwitcherViewModel(JobId job) : ICandidateSwitcherViewModel
{
    private readonly ObservableCollection<CandidateChoice> choices = [];

    public event EventHandler<JobId>? Switched;

    public JobId Job => job;

    public IReadOnlyList<CandidateChoice> Choices => choices;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial string Heading { get; private set; } = string.Empty;

    public void Track(ImmutableDictionary<JobId, BoardContest> contests)
    {
        var contest = contests.TryGetValue(job, out var found) ? Option<ContestRecord>.Some(found.Contest) : Option<ContestRecord>.None;
        IsShown = contest.IsSome;
        Heading = contest.Match(Headed, () => string.Empty);
        choices.ShowOnly(contest.Match<IEnumerable<CandidateChoice>>(Choose, () => []));
    }

    [RelayCommand]
    private void Switch(object? chosen)
    {
        if (chosen is CandidateChoice choice && !choice.IsOpen)
        {
            Switched?.Invoke(this, choice.Job);
        }
    }

    private static string Headed(ContestRecord contest) =>
        $"{ContestPhrases.Candidates(contest.Candidates.Count)} · {ContestPhrases.Status(contest)}";

    private IEnumerable<CandidateChoice> Choose(ContestRecord contest) =>
        contest.Candidates.Select(candidate =>
        {
            var winner = contest.Verdict.Bind(verdict => verdict.Winner) == Option<JobId>.Some(candidate.Job);
            var standing = contest.Verdict.Bind(verdict => verdict.Ranking.FirstOrDefault(found => found.Facts.Job == candidate.Job).ToOption());

            return new CandidateChoice(
                candidate.Job,
                string.Create(CultureInfo.InvariantCulture, $"Candidate {candidate.Ordinal}{(winner ? " · winner" : string.Empty)}"),
                standing.Match(ContestPhrases.Choice, () => ContestPhrases.Outcome(candidate.Outcome)),
                candidate.Job == job,
                winner);
        });
}
