using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Documentation.Contracts;
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

    IAsyncRelayCommand SwitchCommand { get; }

    IAsyncRelayCommand CreateSiteCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class DocsSettingViewModel(IDocumentation documentation, IRoleSettings roles) : IDocsSettingViewModel
{
    private string repository = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SwitchCommand))]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial bool IsOn { get; private set; }

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

    public async Task ReadAsync(string shown, CancellationToken cancellationToken)
    {
        repository = shown;
        var docs = await documentation.OfRepositoryAsync(shown, cancellationToken);
        IsOn = docs.Enabled;
        Folder = docs.Folder;
        Writer = DocsPhrases.Writer(await roles.OfRoleAsync(shown, ChildRole.Research, cancellationToken));
        Note = DocsPhrases.Committed;
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
        Site = created.Match(done => DocsPhrases.Created(done, Site), _ => Site);
    }

    private void ShowSite(DocsSite site)
    {
        CanCreateSite = !site.Exists;
        Site = DocsPhrases.Site(site);
    }

    [RelayCommand(CanExecute = nameof(IsShown))]
    private async Task SwitchAsync(CancellationToken cancellationToken)
    {
        var switched = await documentation.SwitchAsync(repository, !IsOn, cancellationToken);

        switched.Match<Action>(
            rules => () =>
            {
                IsOn = rules.Enabled;
                Folder = rules.Folder;
                Note = DocsPhrases.Saved;
                Error = string.Empty;
            },
            error => () => Error = DocsPhrases.NotSwitched(error))();
        OnPropertyChanged(nameof(IsOn));
    }
}

internal static class DocsPhrases
{
    public const string Title = "Document features as they are built";

    public const string Description = "Each plan becomes a version with its C4; every change you ask for is recorded.";

    public const string Committed = "Read from .avala/jobs.json. New jobs pick it up once it is committed.";

    public const string Saved = "Saved to .avala/jobs.json. New jobs pick it up once it is committed.";

    public static string Writer(ModelChoice research) =>
        research.Model.IsNone && research.Effort.IsNone
            ? "research · the harness's default model"
            : "research" + research.Model.Match(model => $" · {model}", () => string.Empty) + research.Effort.Match(effort => $" · {effort}", () => string.Empty);

    public static string Site(DocsSite site) =>
        !site.Exists ? "DocFX, laid out like Microsoft Learn, with the C4 player."
        : site.CanServe ? $"In {site.Root}. Open it from a job's Docs."
        : $"In {site.Root}. To open it, install DocFX: {DocsSite.Install}";

    public static string Created(SiteCreated created, string site) =>
        $"{site} Wrote {(created.Written.Count == 1 ? "1 file" : $"{created.Written.Count} files")}." + created.Filler.Match(_ => " A job fills the overview, quickstart, architecture and configuration.", () => string.Empty);

    public static string NotCreated(DocumentationError error) => error == DocumentationError.Unwritable ? "The site's files could not be written." : "The repository could not be read.";

    public static string NotSwitched(DocumentationError error) => error switch
    {
        DocumentationError.Malformed => ".avala/jobs.json is not valid JSON, so it was left as it is. Fix it, then switch again.",
        DocumentationError.Unwritable => ".avala/jobs.json could not be written.",
        _ => ".avala/jobs.json could not be read.",
    };
}
