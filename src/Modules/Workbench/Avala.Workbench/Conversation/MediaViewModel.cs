using Avala.Agents.Contracts.Sessions;
using Avala.Components.Pictures;
using Avala.Jobs.Contracts;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IMediaViewModel
{
    string Name { get; }

    string FilePath { get; }

    string Caption { get; }

    bool HasCaption { get; }

    IPicture Picture { get; }

    IAsyncRelayCommand<bool> ShowCommand { get; }

    IRelayCommand OpenCommand { get; }

    string Notice { get; }

    bool HasNotice { get; }

    IAsyncRelayCommand CopyCommand { get; }

    IAsyncRelayCommand CopyMarkdownCommand { get; }

    IAsyncRelayCommand CopyLinkCommand { get; }

    IAsyncRelayCommand AddToNoteCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class MediaViewModel(JobId job, MediaEntry entry, IPictureDecoder pictures, MediaActions actions) : IMediaViewModel, ITimelineItem
{
    public const int PictureWidth = 720;

    public event EventHandler? OpenRequested;

    public Attachment Media { get; } = entry.Media;

    public ShownImage Shown { get; } = new(entry.Media, entry.Caption);

    public string Name => Media.Name;

    public string FilePath => Media.Path;

    public string Caption { get; } = entry.Caption;

    public bool HasCaption => Caption.Length > 0;

    public bool IsShown => true;

    public bool HasNotice => Notice.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string Notice { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IPicture Picture { get; private set; } = NoPicture.Instance;

    public void Update(ITimelineEntry entry)
    {
    }

    [RelayCommand]
    private void Open() => OpenRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task CopyAsync(CancellationToken cancellationToken) =>
        Notice = await actions.Clipboard.CopyImageAsync(Media.Path, cancellationToken) ? MediaPhrases.Copied : MediaPhrases.NotCopied;

    [RelayCommand]
    private async Task CopyMarkdownAsync(CancellationToken cancellationToken) =>
        Notice = await actions.Clipboard.CopyAsync(MediaPhrases.Markdown(Name, Caption, Media.Path), cancellationToken) ? MediaPhrases.Copied : MediaPhrases.NotCopied;

    [RelayCommand]
    private async Task CopyLinkAsync(CancellationToken cancellationToken) =>
        Notice = await actions.Clipboard.CopyLinkAsync(new Uri(Path.GetFullPath(Media.Path)), cancellationToken) ? MediaPhrases.Copied : MediaPhrases.NotCopied;

    [RelayCommand]
    private async Task AddToNoteAsync(CancellationToken cancellationToken) =>
        Notice = await actions.Noting.AddAsync(job, Media, Caption, cancellationToken) ? MediaPhrases.AddedToNote : MediaPhrases.NotAddedToNote;

    [RelayCommand]
    private async Task ShowAsync(bool shown, CancellationToken cancellationToken)
    {
        if (!shown)
        {
            Release();
            return;
        }

        if (!Picture.IsNothing)
        {
            return;
        }

        try
        {
            var encoded = await File.ReadAllBytesAsync(Media.Path, cancellationToken);
            Picture = await pictures.DecodeAsync(encoded, PictureWidth, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Picture = NoPicture.Instance;
        }
    }

    private void Release()
    {
        var shown = Picture;
        Picture = NoPicture.Instance;
        shown.Dispose();
    }
}
