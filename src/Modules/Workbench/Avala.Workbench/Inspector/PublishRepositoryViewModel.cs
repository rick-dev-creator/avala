using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Workbench.RepositoryChoice;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Inspector;

internal interface IPublishRepositoryViewModel
{
    bool IsShown { get; }

    string Remote { get; }

    bool CanOffer { get; }

    bool IsOpen { get; }

    IPublishOptionsViewModel Options { get; }

    string Error { get; }

    string Notice { get; }

    IAsyncRelayCommand StartCommand { get; }

    IAsyncRelayCommand PublishCommand { get; }

    IRelayCommand CancelCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class PublishRepositoryViewModel : IPublishRepositoryViewModel
{
    private readonly IRepositoryPublishing publishing;
    private readonly PublishOptionsViewModel options;
    private Option<string> repository;
    private int reads;

    public PublishRepositoryViewModel(IRepositoryPublishing publishing, PublishOptionsViewModel options)
    {
        this.publishing = publishing;
        this.options = options;
        options.Changed += (_, _) => PublishCommand.NotifyCanExecuteChanged();
    }

    public IPublishOptionsViewModel Options => options;

    public PublishOptionsViewModel Choosing => options;

    public Task Reading { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial string Remote { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool CanOffer { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PublishCommand))]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Notice { get; private set; } = string.Empty;

    public void Show(Option<string> shown)
    {
        if (shown == repository)
        {
            return;
        }

        repository = shown;
        IsOpen = false;
        Error = string.Empty;
        Notice = string.Empty;
        Reading = ReadAsync(CancellationToken.None);
    }

    [RelayCommand(CanExecute = nameof(CanOffer))]
    private async Task StartAsync(CancellationToken cancellationToken)
    {
        Error = string.Empty;
        IsOpen = true;
        await options.LoadAsync(repository.Match(Leaf, () => string.Empty), cancellationToken);
        PublishCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanPublish))]
    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        var published = await repository.Match(
            found => options.PublishAsync(found, cancellationToken),
            () => Task.FromResult(Result<PublishedRepository, ForgeError>.Failure(ForgeError.NotFound)));
        Error = published.Match(_ => string.Empty, PublishPhrases.Failure);
        Notice = published.Match(PublishPhrases.Published, _ => string.Empty);
        IsOpen = !published.IsSuccess;
        Reading = ReadAsync(cancellationToken);
        await Reading;
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        Error = string.Empty;
    }

    private bool CanPublish() => IsOpen && options.IsReady;

    private static string Leaf(string path) => Path.GetFileName(path.TrimEnd('/', '\\'));

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        var ticket = ++reads;
        var remote = await repository.Match(
            async found => Option<Option<RepositoryRemote>>.Some(await publishing.RemoteOfAsync(found, cancellationToken)),
            () => Task.FromResult(Option<Option<RepositoryRemote>>.None));

        if (ticket != reads)
        {
            return;
        }

        IsShown = remote.IsSome;
        CanOffer = remote.Match(found => found.IsNone, () => false);
        Remote = remote.Match(found => found.Match(PublishPhrases.Remote, () => PublishPhrases.NoRemote), () => string.Empty);
    }
}
