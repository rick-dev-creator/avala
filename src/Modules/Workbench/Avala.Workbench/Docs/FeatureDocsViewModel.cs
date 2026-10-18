using Avala.Documentation.Contracts;
using Avala.Sdk;
using Avala.Workbench.Documenting;
using Avala.Workbench.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Docs;

internal interface IFeatureDocsViewModel
{
    string Title { get; }

    string Feature { get; }

    string Where { get; }

    IC4PlayerViewModel Player { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FeatureDocsViewModel : IFeatureDocsViewModel, IPage, IRecipient<FeatureDocsRequested>
{
    private readonly IDocumentation documentation;
    private readonly JobFocus focus;
    private readonly C4PlayerViewModel player;

    public FeatureDocsViewModel(IDocumentation documentation, JobFocus focus, IMessenger messenger, C4PlayerViewModel player)
    {
        this.documentation = documentation;
        this.focus = focus;
        this.player = player;
        messenger.Register(this);
    }

    public string Title => "Docs";

    public PagePlacement Placement => PagePlacement.Hidden;

    public IC4PlayerViewModel Player => player;

    public C4PlayerViewModel Played => player;

    public Task Loading { get; private set; } = Task.CompletedTask;

    [ObservableProperty]
    public partial string Feature { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Where { get; private set; } = string.Empty;

    public void Receive(FeatureDocsRequested message)
    {
        Feature = message.Feature.Title;
        Where = $"{Path.GetFileName(message.Repository.TrimEnd('/', '\\'))} · {message.Feature.Slug}"
            + message.Branch.Match(branch => $" · not merged, on {Review.ReviewPhrases.Short(branch)}", () => string.Empty);
        Loading = LoadAsync(message, CancellationToken.None);
        focus.Show(this);
    }

    private async Task LoadAsync(FeatureDocsRequested message, CancellationToken cancellationToken) =>
        player.Show(await documentation.StoryAsync(message.Folder.Match(folder => folder, () => message.Repository), message.Feature, cancellationToken));
}
