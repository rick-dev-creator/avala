using System.Collections.ObjectModel;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Accounts;
using Avala.Workbench.Readying;
using Avala.Workbench.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Readiness;

internal interface IReadinessCardViewModel
{
    bool IsShown { get; }

    IReadOnlyList<IReadinessItemViewModel> Items { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ReadinessCardViewModel(RepositoryReadiness readiness, ReadinessActions actions)
    : IReadinessCardViewModel, IRegionAware<JobId>, IActivatable, IPresentation
{
    private readonly ObservableCollection<ReadinessItemViewModel> items = [];
    private Option<JobId> job;
    private Option<ReadinessFacts> shown;

    public event EventHandler<Presented>? Presented;

    public long Revision { get; private set; }

    public Task Loading { get; private set; } = Task.CompletedTask;

    public IReadOnlyList<IReadinessItemViewModel> Items => items;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    public void OnRegionContextChanged(Option<JobId> context)
    {
        job = context;
        shown = Option<ReadinessFacts>.None;
        items.Clear();
        IsShown = false;
        Loading = OpenAsync(context, CancellationToken.None);
    }

    public void Activate()
    {
        if (IsShown)
        {
            Loading = RefreshAsync(CancellationToken.None);
        }
    }

    public void Deactivate()
    {
    }

    private async Task OpenAsync(Option<JobId> context, CancellationToken cancellationToken)
    {
        var facts = await context.Match(
            found => readiness.OfJobAsync(found, cancellationToken).AsTask(),
            () => Task.FromResult(Option<ReadinessFacts>.None));

        if (job == context)
        {
            shown = facts;

            foreach (var found in facts.Match<ReadinessFacts[]>(read => [read], () => []))
            {
                Show(found);
            }
        }

        Present();
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        foreach (var repository in shown.Match<string[]>(facts => [facts.Repository], () => []))
        {
            var facts = await readiness.ReadAsync(repository, cancellationToken);

            if (shown.Match(current => current.Repository == repository, () => false))
            {
                shown = facts;
                Show(facts);
            }
        }

        Present();
    }

    private void Show(ReadinessFacts facts)
    {
        var steps = ReadinessPhrases.Steps(facts);

        foreach (var item in items)
        {
            var step = steps.FirstOrDefault(found => found.Need == item.Need);

            if (step is null)
            {
                item.Finish(ReadinessPhrases.Done(item.Need, facts));
            }
            else
            {
                item.Show(step, Act(step, facts), Choices(step, facts));
            }
        }

        foreach (var step in steps.Where(found => items.All(item => item.Need != found.Need)))
        {
            items.Add(new ReadinessItemViewModel(step, Act(step, facts), Choices(step, facts)));
        }

        IsShown = items.Count > 0;
    }

    private Func<CancellationToken, Task> Act(ReadinessStep step, ReadinessFacts facts) => step.Need switch
    {
        ReadinessNeed.Forge when step.Accounts.Count == 0 => RefreshAsync,
        ReadinessNeed.Forge => token => ConnectAsync(step.Accounts[0], facts, token),
        ReadinessNeed.Rules => token => CommitAsync(facts.Repository, token),
        _ => _ => actions.DeclareChecksAsync(facts.Repository),
    };

    private ReadinessAccountViewModel[] Choices(ReadinessStep step, ReadinessFacts facts) =>
        step.Accounts.Count < 2
            ? []
            : [.. step.Accounts.Select(account => new ReadinessAccountViewModel(
                ReadinessPhrases.Login(account),
                ReadinessPhrases.Tool(account, facts.Forge.SignIn),
                token => ConnectAsync(account, facts, token)))];

    private async Task ConnectAsync(AccountStatus account, ReadinessFacts facts, CancellationToken cancellationToken)
    {
        var connected = await actions.ConnectAsync(account, facts.Forge.Forge, cancellationToken);
        Report(ReadinessNeed.Forge, connected.Match(_ => string.Empty, AccountPhrases.Refusal));
        await RefreshAsync(cancellationToken);
    }

    private async Task CommitAsync(string repository, CancellationToken cancellationToken)
    {
        var committed = await actions.CommitRulesAsync(repository, cancellationToken);
        Report(ReadinessNeed.Rules, committed.Match(_ => string.Empty, DocsPhrases.NotCommitted));
        await RefreshAsync(cancellationToken);
    }

    private void Report(ReadinessNeed need, string error)
    {
        foreach (var item in items.Where(found => found.Need == need))
        {
            item.Error = error;
        }
    }

    private void Present()
    {
        Revision++;
        Presented?.Invoke(this, new Presented(Revision));
    }
}
