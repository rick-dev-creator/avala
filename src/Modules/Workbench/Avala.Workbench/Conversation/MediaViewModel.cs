using Avala.Agents.Contracts.Sessions;
using Avala.Components.Pictures;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IMediaViewModel
{
    string Name { get; }

    string Caption { get; }

    bool HasCaption { get; }

    IPicture Picture { get; }

    IAsyncRelayCommand<bool> ShowCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class MediaViewModel(MediaEntry entry, IPictureDecoder pictures) : IMediaViewModel, ITimelineItem
{
    public const int PictureWidth = 720;

    public Attachment Media { get; } = entry.Media;

    public string Name => Media.Name;

    public string Caption { get; } = entry.Caption;

    public bool HasCaption => Caption.Length > 0;

    public bool IsShown => true;

    [ObservableProperty]
    public partial IPicture Picture { get; private set; } = NoPicture.Instance;

    public void Update(ITimelineEntry entry)
    {
    }

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
