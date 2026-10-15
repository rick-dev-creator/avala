using Avala.Agents.Contracts.Sessions;
using Avala.Components.Pictures;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IAttachmentChipViewModel
{
    string Name { get; }

    string Kind { get; }

    bool IsImage { get; }

    IPicture Picture { get; }

    bool CanRemove { get; }

    IRelayCommand RemoveCommand { get; }

    IAsyncRelayCommand<bool> ShowCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class AttachmentChipViewModel(Attachment attachment, IPictureDecoder pictures, bool removable) : IAttachmentChipViewModel, IDisposable
{
    public const int ThumbnailWidth = 96;

    public event EventHandler? Removed;

    public Attachment Attachment { get; } = attachment;

    public string Name => Attachment.Name;

    public string Kind => Path.GetExtension(Attachment.Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 } extension ? extension : "FILE";

    public bool IsImage => Attachment.IsImage;

    public bool CanRemove { get; } = removable;

    [ObservableProperty]
    public partial IPicture Picture { get; private set; } = NoPicture.Instance;

    public void Dispose() => Release();

    [RelayCommand]
    private void Remove() => Removed?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task ShowAsync(bool shown, CancellationToken cancellationToken)
    {
        if (!shown || !IsImage)
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
            var encoded = await File.ReadAllBytesAsync(Attachment.Path, cancellationToken);
            Picture = await pictures.DecodeAsync(encoded, ThumbnailWidth, cancellationToken);
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
