using System.Collections.ObjectModel;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Components.Privacy;
using Avala.Verification.Contracts;
using Avala.Workbench.Accounts;
using Avala.Workbench.Presenting;
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
internal sealed partial class ReadinessCardViewModel(RepositoryReadiness readiness, ReadinessActions actions, BoardFeed feed)
    : IReadinessCardViewModel, IRegionAware<JobId>, IActivatable, IPresentation
{
    private readonly ObservableCollection<ReadinessItemViewModel> items = [];
    private Option<JobId> job;
    private Option<ReadinessFacts> shown;
    private Option<ChecksDiscovery> discovery;
    private bool checksOnly;

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
        checksOnly = false;
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

        feed.Start(_ => Rediscovered() ? [RediscoveredAsync] : []);
    }

    public void Deactivate() => feed.Stop();

    private bool Rediscovered() => Current().Map(State) != discovery.Map(State) && (shown.IsSome || Current().IsSome);

    private Option<ChecksDiscovery> Current() => job.Match(found => feed.DiscoveryOf(found), () => Option<ChecksDiscovery>.None);

    private static (DiscoveryState, int) State(ChecksDiscovery found) => (found.State, found.Checks.Count);

    private Task RediscoveredAsync(CancellationToken cancellationToken)
    {
        var repository = shown.Map(facts => facts.Repository).Match(found => Option<string>.Some(found), () => Current().Map(found => found.Repository));

        return repository.Match(
            found => feed.LoadAsync(
                token => readiness.ReadAsync(found, token),
                read =>
                {
                    checksOnly = checksOnly || shown.IsNone;
                    shown = read;
                    Show(read);
                    Present();
                }),
            () => Task.CompletedTask);
    }

    private async Task OpenAsync(Option<JobId> context, CancellationToken cancellationToken)
    {
        var facts = await context.Match(
            found => readiness.OfJobAsync(found, cancellationToken).AsTask(),
            () => Task.FromResult(Option<ReadinessFacts>.None));
        var later = facts.IsNone ? context.Bind(feed.DiscoveryOf) : Option<ChecksDiscovery>.None;

        facts = await later.Match(
            async found => Option<ReadinessFacts>.Some(await readiness.ReadAsync(found.Repository, cancellationToken)),
            () => Task.FromResult(facts));

        if (job == context)
        {
            checksOnly = later.IsSome;
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
        discovery = job.Match(found => feed.DiscoveryOf(found), () => Option<ChecksDiscovery>.None);
        var steps = ReadinessPhrases.Steps(facts, discovery).Where(step => !checksOnly || step.Need == ReadinessNeed.Checks).ToList();

        foreach (var item in items)
        {
            var step = steps.FirstOrDefault(found => found.Need == item.Need);

            if (step is null)
            {
                item.Finish(ReadinessPhrases.Done(item.Need, facts, discovery));
            }
            else
            {
                item.Show(step, Act(step, facts), Choices(step, facts));
                item.Dismissing(Skip(step));
            }
        }

        foreach (var step in steps.Where(found => items.All(item => item.Need != found.Need)))
        {
            var added = new ReadinessItemViewModel(step, Act(step, facts), Choices(step, facts));
            added.Dismissing(Skip(step));
            items.Add(added);
        }

        if (ReadinessPhrases.FoundChecks(facts, discovery) && items.All(item => item.Need != ReadinessNeed.Checks))
        {
            var found = new ReadinessItemViewModel(new ReadinessStep(ReadinessNeed.Checks, PersonalPhrase.Empty, string.Empty), _ => Task.CompletedTask, []);
            found.Finish(ReadinessPhrases.Done(ReadinessNeed.Checks, facts, discovery));
            items.Add(found);
        }

        IsShown = items.Count > 0;
    }

    private Func<CancellationToken, Task> Act(ReadinessStep step, ReadinessFacts facts) => step.Need switch
    {
        ReadinessNeed.Forge when step.Accounts.Count == 0 => RefreshAsync,
        ReadinessNeed.Forge => token => ConnectAsync(step.Accounts[0], facts, token),
        ReadinessNeed.Rules => token => CommitAsync(facts.Repository, token),
        _ when step.Dismiss.Length > 0 => token => DecideAsync(actions.UseChecksAsync, token),
        _ => _ => actions.DeclareChecksAsync(facts.Repository),
    };

    private Func<CancellationToken, Task> Skip(ReadinessStep step) =>
        step.Dismiss.Length > 0 ? token => DecideAsync(actions.SkipChecksAsync, token) : _ => Task.CompletedTask;

    private Task DecideAsync(Func<JobId, CancellationToken, Task> decide, CancellationToken cancellationToken) =>
        job.Match(found => decide(found, cancellationToken), () => Task.CompletedTask);

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
