using Avala.Forges.Contracts;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.RepositoryChoice;

internal interface IPublishOptionsViewModel
{
    IReadOnlyList<string> Targets { get; }

    string Target { get; set; }

    string ForgeKind { get; }

    string Name { get; set; }

    bool IsPrivate { get; set; }

    bool IsPublic { get; set; }

    string Problem { get; }
}

[INotifyPropertyChanged]
internal sealed partial class PublishOptionsViewModel(IRepositoryPublishing publishing) : IPublishOptionsViewModel
{
    private IReadOnlyList<PublishTarget> known = [];
    private string suggested = string.Empty;

    public event EventHandler? Changed;

    public Task Loading { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial IReadOnlyList<string> Targets { get; private set; } = [];

    [ObservableProperty]
    public partial string Target { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ForgeKind { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPublic))]
    public partial bool IsPrivate { get; set; } = true;

    public bool IsPublic
    {
        get => !IsPrivate;
        set => IsPrivate = !value;
    }

    [ObservableProperty]
    public partial string Problem { get; private set; } = string.Empty;

    public bool IsReady => Selected.IsSome && Name.Trim().Length > 0;

    private Option<PublishTarget> Selected => known.FirstOrDefault(target => target.Forge.Value == Target).ToOption();

    public Task LoadAsync(string name, CancellationToken cancellationToken)
    {
        Suggest(name);
        Loading = ReadAsync(cancellationToken);

        return Loading;
    }

    public void Suggest(string name)
    {
        if (Name == suggested)
        {
            Name = name.Trim();
        }

        suggested = name.Trim();
    }

    public async Task<Result<PublishedRepository, ForgeError>> PublishAsync(string repository, CancellationToken cancellationToken) =>
        await Selected.Match(
            async target => await publishing.PublishAsync(repository, target.Forge, new RepositoryDraft(Name.Trim(), IsPrivate), cancellationToken),
            () => Task.FromResult(Result<PublishedRepository, ForgeError>.Failure(ForgeError.UnknownConnection)));

    partial void OnTargetChanged(string value)
    {
        ForgeKind = Selected.Match(target => target.ForgeKind, () => string.Empty);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnNameChanged(string value) => Changed?.Invoke(this, EventArgs.Empty);

    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        known = await publishing.TargetsAsync(cancellationToken);
        Targets = [.. known.Select(target => target.Forge.Value)];
        Problem = known.Count == 0 ? PublishPhrases.NoTargets : string.Empty;

        if (Selected.IsNone)
        {
            Target = Targets.Count > 0 ? Targets[0] : string.Empty;
        }
    }
}
