using System.Text;
using Avala.Components.Canvases;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Linking;
using Avala.Workbench.Reviewing;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal enum PreviewKind
{
    None,
    Image,
    Svg,
    Page,
}

internal interface IFilePreviewViewModel
{
    string Name { get; }

    PreviewKind Kind { get; }

    string Hint { get; }

    bool IsOpen { get; }

    bool HasThumbnail { get; }

    bool IsImage { get; }

    byte[] Picture { get; }

    CanvasRendering Drawing { get; }

    string Notice { get; }

    IAsyncRelayCommand OpenCommand { get; }

    IAsyncRelayCommand LoadCommand { get; }

    IRelayCommand CloseCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FilePreviewViewModel : IFilePreviewViewModel
{
    public const int ThumbnailLimit = 512 * 1024;

    public const int SheetLimit = 16 * 1024 * 1024;

    private readonly JobId job;
    private readonly string path;
    private readonly WorkedFiles files;
    private readonly Links links;

    public FilePreviewViewModel(JobId job, string path, WorkedFiles files, Links links)
    {
        this.job = job;
        this.path = path;
        this.files = files;
        this.links = links;
        Name = System.IO.Path.GetFileName(path);
        Kind = KindOf(path);
        Picture = [];
        Notice = string.Empty;
    }

    public string Name { get; }

    public PreviewKind Kind { get; }

    public string Hint => Kind == PreviewKind.Page ? ConversationPhrases.OpenInBrowser : ConversationPhrases.Preview;

    public bool IsImage => Kind == PreviewKind.Image;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial bool HasThumbnail { get; private set; }

    [ObservableProperty]
    public partial byte[] Picture { get; private set; }

    [ObservableProperty]
    public partial CanvasRendering Drawing { get; private set; } = CanvasRendering.Nothing;

    [ObservableProperty]
    public partial string Notice { get; private set; }

    public static PreviewKind KindOf(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" => PreviewKind.Image,
        ".svg" => PreviewKind.Svg,
        ".html" or ".htm" => PreviewKind.Page,
        _ => PreviewKind.None,
    };

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (Kind is PreviewKind.Image or PreviewKind.Svg)
        {
            Show(await files.ReadAsync(job, path, SheetLimit, cancellationToken));
        }
    }

    [RelayCommand]
    private async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (Kind == PreviewKind.Page)
        {
            var located = await files.LocateAsync(job, path, cancellationToken);
            Notice = await located.Match(
                found => found.Match(
                    async file => (await links.OpenFileAsync(file, cancellationToken)).Match(_ => string.Empty, _ => ConversationPhrases.NotOpened(Name)),
                    () => Task.FromResult(ConversationPhrases.Missing(Name))),
                _ => Task.FromResult(ConversationPhrases.Missing(Name)));

            return;
        }

        if (Picture.Length == 0 && Drawing.IsNothing)
        {
            await LoadAsync(cancellationToken);
        }

        IsOpen = Notice.Length == 0;
    }

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Close() => IsOpen = false;

    private void Show(Result<Option<byte[]>, WorkspaceFailure> read)
    {
        var bytes = read.Match(found => found.Match(content => content, () => []), _ => []);
        Notice = bytes.Length > 0 ? string.Empty
            : read.Match(_ => ConversationPhrases.Missing(Name), failure => failure == WorkspaceFailure.FileTooLarge ? ConversationPhrases.TooLargeToPreview(Name) : ConversationPhrases.Missing(Name));

        if (Kind == PreviewKind.Svg && bytes.Length > 0)
        {
            Drawing = new CanvasRendering(CanvasMediaTypes.Svg, Encoding.UTF8.GetString(bytes), true, 1);
        }
        else
        {
            Picture = bytes;
        }

        HasThumbnail = bytes.Length is > 0 and <= ThumbnailLimit;
    }
}
