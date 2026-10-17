using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface IDocsSettingViewModel
{
    bool IsShown { get; }

    bool IsOn { get; }

    string Folder { get; }

    string Writer { get; }

    string Note { get; }

    string Error { get; }

    string Site { get; }

    bool CanCreateSite { get; }

    bool IsSitePreparing { get; }

    IRelayCommand OpenSiteJobCommand { get; }

    bool IsUncommitted { get; }

    IAsyncRelayCommand SwitchCommand { get; }

    IAsyncRelayCommand CommitCommand { get; }

    IAsyncRelayCommand CreateSiteCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class DocsSettingViewModel(IDocumentation documentation, IRoleSettings roles, JobFocus focus) : IDocsSettingViewModel
{
    private string repository = string.Empty;
    private Option<JobId> siteJob;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchCommand))]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial bool IsOn { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    public partial bool IsUncommitted { get; private set; }

    [ObservableProperty]
    public partial string Folder { get; private set; } = DocsRules.DefaultFolder;

    [ObservableProperty]
    public partial string Writer { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Note { get; private set; } = DocsPhrases.Committed;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Site { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateSiteCommand))]
    public partial bool CanCreateSite { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSiteJobCommand))]
    public partial bool IsSitePreparing { get; private set; }

    public async Task ReadAsync(string shown, CancellationToken cancellationToken)
    {
        repository = shown;
        await ShowRulesAsync(cancellationToken);
        Writer = DocsPhrases.Writer(await roles.OfRoleAsync(shown, ChildRole.Research, cancellationToken));
        Error = string.Empty;
        ShowSite(await documentation.SiteAsync(shown, cancellationToken));
        IsShown = true;
    }

    [RelayCommand(CanExecute = nameof(CanCreateSite))]
    private async Task CreateSiteAsync(CancellationToken cancellationToken)
    {
        var created = await documentation.CreateSiteAsync(repository, cancellationToken);
        Error = created.Match(_ => string.Empty, DocsPhrases.NotCreated);
        ShowSite(await documentation.SiteAsync(repository, cancellationToken));
    }

    [RelayCommand(CanExecute = nameof(IsSitePreparing))]
    private void OpenSiteJob()
    {
        foreach (var job in siteJob.Match<JobId[]>(found => [found], () => []))
        {
            focus.Select(job);
        }
    }

    private void ShowSite(DocsSite site)
    {
        siteJob = site.Preparing;
        IsSitePreparing = site.Preparing.IsSome;
        CanCreateSite = !site.Exists && site.Preparing.IsNone;
        Site = DocsPhrases.Site(site);
    }

    [RelayCommand(CanExecute = nameof(IsShown))]
    private async Task SwitchAsync(CancellationToken cancellationToken)
    {
        var switched = await documentation.SwitchAsync(repository, !IsOn, cancellationToken);

        Error = switched.Match(_ => string.Empty, DocsPhrases.NotSwitched);
        await ShowRulesAsync(cancellationToken);
        OnPropertyChanged(nameof(IsOn));
    }

    [RelayCommand(CanExecute = nameof(IsUncommitted))]
    private async Task CommitAsync(CancellationToken cancellationToken)
    {
        var committed = await documentation.CommitRulesAsync(repository, cancellationToken);
        Error = committed.Match(_ => string.Empty, DocsPhrases.NotCommitted);
        await ShowRulesAsync(cancellationToken);
        Note = committed.Match(DocsPhrases.JustCommitted, _ => Note);
    }

    private async Task ShowRulesAsync(CancellationToken cancellationToken)
    {
        var rules = await documentation.RulesAsync(repository, cancellationToken);
        IsOn = rules.Working.Enabled;
        Folder = rules.Working.Folder;
        IsUncommitted = rules.Working != rules.Committed;
        Note = IsUncommitted ? DocsPhrases.Uncommitted(IsOn) : DocsPhrases.Committed;
    }
}

internal static class DocsPhrases
{
    public const string Title = "Document features as they are built";

    public const string Description = "Each plan becomes a version with its C4; every change you ask for is recorded.";

    public const string Committed = "Read from .avala/jobs.json as committed. New jobs follow it.";

    public static string Uncommitted(bool on) =>
        on
            ? "On in your working copy only: new jobs run without docs until .avala/jobs.json is committed."
            : "Off in your working copy only: new jobs keep writing docs until .avala/jobs.json is committed.";

    public static string JustCommitted(DocsCommit commit) => $"Committed .avala/jobs.json as {commit.Commit[..Math.Min(7, commit.Commit.Length)]} on {commit.Branch}. New jobs follow it.";

    public static string NotCommitted(DocumentationError error) => error switch
    {
        DocumentationError.NotOnABranch => "Not committed: the repository is not on a branch. Check out a branch, then commit again.",
        DocumentationError.NoGitIdentity => "Not committed: git has no name and email for you. Set user.name and user.email, then commit again.",
        DocumentationError.NothingToCommit => "Nothing to commit: .avala/jobs.json is already as committed.",
        _ => "Not committed: git refused the commit. Commit .avala/jobs.json yourself.",
    };

    public static string Writer(ModelChoice research) =>
        research.Model.IsNone && research.Effort.IsNone
            ? "research · the harness's default model"
            : "research" + research.Model.Match(model => $" · {model}", () => string.Empty) + research.Effort.Match(effort => $" · {effort}", () => string.Empty);

    public static string Site(DocsSite site) =>
        site.Preparing.IsSome ? "Being prepared by its job. Review it like any job: the site is here once it is merged."
        : !site.Exists ? "DocFX, laid out like Microsoft Learn, with the C4 player."
        : site.CanServe ? $"In {site.Root}. Open it from a job's Docs."
        : $"In {site.Root}. To open it, install DocFX: {DocsSite.Install}";

    public static string NotCreated(DocumentationError error) => error == DocumentationError.NotSubmitted ? "The site's job could not be submitted." : "The repository could not be read.";

    public static string NotSwitched(DocumentationError error) => error switch
    {
        DocumentationError.Malformed => ".avala/jobs.json is not valid JSON, so it was left as it is. Fix it, then switch again.",
        DocumentationError.Unwritable => ".avala/jobs.json could not be written.",
        _ => ".avala/jobs.json could not be read.",
    };
}
