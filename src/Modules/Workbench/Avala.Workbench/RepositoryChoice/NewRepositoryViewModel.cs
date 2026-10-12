using Avala.Components.Privacy;
using Avala.Sdk;
using Avala.Workbench.Submitting;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.RepositoryChoice;

internal interface INewRepositoryViewModel
{
    bool IsOpen { get; }

    string Parent { get; }

    string Name { get; set; }

    bool IsPublishing { get; set; }

    IPublishOptionsViewModel Publish { get; }

    string Error { get; }

    IAsyncRelayCommand StartCommand { get; }

    IAsyncRelayCommand CreateCommand { get; }

    IRelayCommand CancelCommand { get; }
}

internal sealed record RepositoryCreated(string Path, PersonalPhrase Notice);

[INotifyPropertyChanged]
internal sealed partial class NewRepositoryViewModel : INewRepositoryViewModel
{
    private readonly ProjectsPlace projects;
    private readonly IRepositories repositories;
    private readonly PublishOptionsViewModel publish;

    public NewRepositoryViewModel(ProjectsPlace projects, IRepositories repositories, PublishOptionsViewModel publish)
    {
        this.projects = projects;
        this.repositories = repositories;
        this.publish = publish;
        publish.Changed += (_, _) => CreateCommand.NotifyCanExecuteChanged();
    }

    public event EventHandler<RepositoryCreated>? Created;

    public IPublishOptionsViewModel Publish => publish;

    public PublishOptionsViewModel Publishing => publish;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Parent { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial bool IsPublishing { get; set; }

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    partial void OnNameChanged(string value) => publish.Suggest(value);

    partial void OnIsPublishingChanged(bool value)
    {
        if (value)
        {
            _ = publish.LoadAsync(Name, CancellationToken.None);
        }
    }

    [RelayCommand]
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        var chosen = await projects.PickAsync(RepositoryPhrases.ParentTitle, cancellationToken);

        chosen.Match<Action>(found => () => Open(found), () => () => { })();
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync(CancellationToken cancellationToken)
    {
        var created = await repositories.CreateAsync(Parent, Name, cancellationToken);
        Error = created.Match(_ => string.Empty, RepositoryPhrases.Failure);

        if (!created.TryGetValue(out var repository, out _))
        {
            return;
        }

        var notice = RepositoryPhrases.Created(repository);

        if (IsPublishing)
        {
            var published = await publish.PublishAsync(repository, cancellationToken);
            notice = new PersonalPhrase(notice, " ", published.Match(PublishPhrases.Published, failure => new PersonalPhrase(PublishPhrases.Failure(failure))));
        }

        Cancel();
        Created?.Invoke(this, new RepositoryCreated(repository, notice));
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        IsPublishing = false;
        Name = string.Empty;
        Error = string.Empty;
    }

    private bool CanCreate() => IsOpen && !string.IsNullOrWhiteSpace(Name) && (!IsPublishing || publish.IsReady);

    private void Open(string parent)
    {
        Parent = parent;
        Error = string.Empty;
        IsOpen = true;
    }
}
