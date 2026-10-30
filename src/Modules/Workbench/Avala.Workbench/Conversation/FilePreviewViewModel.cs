using System.Collections.Frozen;
using System.Text;
using Avala.Components.Canvases;
using Avala.Components.Pictures;
using Avala.Jobs.Contracts;
using Avala.Sdk;
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
    Model,
}

internal interface IFilePreviewViewModel
{
    string Name { get; }

    PreviewKind Kind { get; }

    string Hint { get; }

    bool IsArtifact { get; }

    string Caption { get; }

    bool IsOpen { get; }

    bool HasThumbnail { get; }

    bool IsImage { get; }

    IPicture Picture { get; }

    CanvasRendering Drawing { get; }

    string Notice { get; }

    IAsyncRelayCommand OpenCommand { get; }

    IAsyncRelayCommand LoadCommand { get; }

    IAsyncRelayCommand CloseCommand { get; }

    IAsyncRelayCommand<bool> RealizeCommand { get; }

    IAsyncRelayCommand<int> FitCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FilePreviewViewModel : IFilePreviewViewModel
{
    public const int ThumbnailLimit = 512 * 1024;

    public const int SheetLimit = 16 * 1024 * 1024;

    public const int ThumbnailWidth = 520;

    private static readonly FrozenDictionary<string, PreviewKind> Kinds = new Dictionary<string, PreviewKind>(StringComparer.Ordinal)
    {
        [".png"] = PreviewKind.Image,
        [".jpg"] = PreviewKind.Image,
        [".jpeg"] = PreviewKind.Image,
        [".gif"] = PreviewKind.Image,
        [".webp"] = PreviewKind.Image,
        [".svg"] = PreviewKind.Svg,
        [".html"] = PreviewKind.Page,
        [".htm"] = PreviewKind.Page,
        [".glb"] = PreviewKind.Model,
        [".gltf"] = PreviewKind.Model,
        [".stl"] = PreviewKind.Model,
        [".obj"] = PreviewKind.Model,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private readonly JobId job;
    private readonly string path;
    private readonly FileViewing viewing;
    private byte[] encoded = [];
    private bool isRealized;
    private bool isOpening;
    private int sheetWidth;
    private int shownWidth;
    private int asked;

    public FilePreviewViewModel(JobId job, string path, FileViewing viewing)
    {
        this.job = job;
        this.path = path;
        this.viewing = viewing;
        Name = System.IO.Path.GetFileName(path);
        Kind = KindOf(path);
        Notice = string.Empty;
    }

    public string Name { get; }

    public PreviewKind Kind { get; }

    public string Hint => IsArtifact ? ConversationPhrases.ShowInPreview : ConversationPhrases.Preview;

    public bool IsArtifact => Kind is PreviewKind.Page or PreviewKind.Model;

    public string Caption => Kind == PreviewKind.Model ? ConversationPhrases.ModelKind : ConversationPhrases.PageKind;

    public bool IsImage => Kind == PreviewKind.Image;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial bool HasThumbnail { get; private set; }

    [ObservableProperty]
    public partial IPicture Picture { get; private set; } = NoPicture.Instance;

    [ObservableProperty]
    public partial CanvasRendering Drawing { get; private set; } = CanvasRendering.Nothing;

    [ObservableProperty]
    public partial string Notice { get; private set; }

    public static PreviewKind KindOf(string path) =>
        Kinds.GetValueOrDefault(System.IO.Path.GetExtension(path).ToLowerInvariant(), PreviewKind.None);

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (Kind is PreviewKind.Image or PreviewKind.Svg)
        {
            Show(await viewing.Files.ReadAsync(job, path, SheetLimit, cancellationToken));
            await PictureAsync();
        }
    }

    [RelayCommand]
    private async Task OpenAsync(CancellationToken cancellationToken)
    {
        if (IsArtifact)
        {
            var located = await viewing.Files.LocateAsync(job, path, cancellationToken);
            Notice = await located.Match(
                found => found.Match(
                    file => ShowAsync(file, cancellationToken),
                    () => Task.FromResult(ConversationPhrases.Missing(Name))),
                _ => Task.FromResult(ConversationPhrases.Missing(Name)));

            return;
        }

        if (encoded.Length == 0 && Drawing.IsNothing)
        {
            isOpening = true;
            await LoadAsync(cancellationToken);
            isOpening = false;
        }

        IsOpen = Notice.Length == 0;
        await PictureAsync();
    }

    private async Task<string> ShowAsync(string file, CancellationToken cancellationToken) =>
        viewing.Links.ShowFile(job, file) ? string.Empty
        : Kind == PreviewKind.Model ? ConversationPhrases.OpenTheJob(Name)
        : (await viewing.Links.OpenFileAsync(file, cancellationToken)).Match(_ => string.Empty, _ => ConversationPhrases.NotOpened(Name));

    [RelayCommand(CanExecute = nameof(IsOpen), AllowConcurrentExecutions = true)]
    private Task CloseAsync()
    {
        IsOpen = false;

        if (encoded.Length > ThumbnailLimit)
        {
            encoded = [];
        }

        return PictureAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RealizeAsync(bool shown)
    {
        isRealized = shown;

        return PictureAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task FitAsync(int width)
    {
        sheetWidth = width;

        return PictureAsync();
    }

    private async Task PictureAsync()
    {
        var wanted = !IsImage || encoded.Length == 0 ? 0
            : IsOpen ? Math.Max(sheetWidth, ThumbnailWidth)
            : isRealized && HasThumbnail ? ThumbnailWidth
            : 0;
        var asking = ++asked;

        if (wanted == shownWidth)
        {
            return;
        }

        var picture = wanted == 0 ? NoPicture.Instance : await viewing.Pictures.DecodeAsync(encoded, wanted, CancellationToken.None);

        if (asking != asked)
        {
            picture.Dispose();

            return;
        }

        var shown = Picture;
        Picture = picture;
        shownWidth = picture.IsNothing ? 0 : wanted;
        shown.Dispose();
    }

    private void Show(Result<Option<byte[]>, WorkspaceFailure> read)
    {
        var bytes = read.Match(found => found.Match(content => content, () => []), _ => []);
        Notice = bytes.Length > 0 ? string.Empty
            : read.Match(_ => ConversationPhrases.Missing(Name), failure => failure == WorkspaceFailure.FileTooLarge ? ConversationPhrases.TooLargeToPreview(Name) : ConversationPhrases.Missing(Name));

        if (Kind == PreviewKind.Svg && bytes.Length > 0)
        {
            Drawing = new CanvasRendering(CanvasMediaTypes.Svg, Encoding.UTF8.GetString(bytes), true, 1);
        }
        else if (bytes.Length <= ThumbnailLimit || isOpening)
        {
            encoded = bytes;
        }

        HasThumbnail = bytes.Length is > 0 and <= ThumbnailLimit;
    }
}
