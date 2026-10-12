using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Cards;

internal enum AnswerScope
{
    Once,
    Always,
    Everything,
    InRepository,
}

internal interface IAnswerScopeViewModel
{
    AnswerScope Scope { get; }

    bool IsOnce { get; }

    bool IsAlways { get; }

    bool IsEverything { get; }

    bool IsInRepository { get; }

    string AlwaysLabel { get; }

    string EverythingLabel { get; }

    string InRepositoryLabel { get; }

    string InRepositoryHint { get; }

    bool OffersInRepository { get; }

    string AlwaysUnavailable { get; }

    string EverythingUnavailable { get; }

    IRelayCommand<AnswerScope> ChooseCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class AnswerScopeViewModel : IAnswerScopeViewModel
{
    private Option<PolicyDecision> decision;
    private ItemKind kind;

    public AnswerScopeViewModel(ItemKind kind, Option<PolicyDecision> decision)
    {
        this.kind = kind;
        this.decision = decision;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnce), nameof(IsAlways), nameof(IsEverything), nameof(IsInRepository))]
    public partial AnswerScope Scope { get; private set; }

    public bool IsOnce => Scope == AnswerScope.Once;

    public bool IsAlways => Scope == AnswerScope.Always;

    public bool IsEverything => Scope == AnswerScope.Everything;

    public bool IsInRepository => Scope == AnswerScope.InRepository;

    public string AlwaysLabel => ScopePhrases.Always(kind, Offered.Bind(scope => scope.Rule), Contested);

    public string EverythingLabel => ScopePhrases.Everything(Contested);

    public string InRepositoryLabel => CardPhrases.AlwaysInRepository;

    public string InRepositoryHint => RepositoryRule.Match(CardPhrases.AlwaysInRepositoryScope, () => string.Empty);

    public bool OffersInRepository => RepositoryRule.IsSome;

    public string AlwaysUnavailable => ScopePhrases.AlwaysUnavailable(kind, Offered);

    public string EverythingUnavailable => ScopePhrases.EverythingUnavailable(Offered);

    public Remember Remember => Scope switch
    {
        AnswerScope.Always when Offers(AnswerScope.Always) => Contested ? Remember.ForThisContest : Remember.ForThisJob,
        AnswerScope.Everything when Offers(AnswerScope.Everything) => Contested ? Remember.EverythingInThisContest : Remember.EverythingInThisJob,
        AnswerScope.InRepository when Offers(AnswerScope.InRepository) => Remember.InThisRepository,
        _ => Remember.Once,
    };

    private Option<JobScope> Offered => decision.Bind(found => found.Scope);

    private Option<PolicyRule> RepositoryRule => decision.Bind(found => found.RepositoryRule);

    private bool Contested => decision.Match(found => found.Contested, () => false);

    public void Update(ItemKind asked, Option<PolicyDecision> decided)
    {
        kind = asked;
        decision = decided;
        OnPropertyChanged(nameof(AlwaysLabel));
        OnPropertyChanged(nameof(EverythingLabel));
        OnPropertyChanged(nameof(InRepositoryHint));
        OnPropertyChanged(nameof(OffersInRepository));
        OnPropertyChanged(nameof(AlwaysUnavailable));
        OnPropertyChanged(nameof(EverythingUnavailable));
        ChooseCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(Offers))]
    private void Choose(AnswerScope scope) => Scope = scope;

    private bool Offers(AnswerScope scope) => scope switch
    {
        AnswerScope.Always => Offered.Bind(found => found.Rule).IsSome,
        AnswerScope.Everything => Offered.Match(found => found.Raisable, () => false),
        AnswerScope.InRepository => RepositoryRule.IsSome,
        _ => true,
    };
}
