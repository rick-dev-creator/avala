using Avala.Components.Pictures;
using Avala.Notes.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Notes;

internal interface INoteImageViewModel
{
    string Name { get; }

    IPicture Picture { get; }

    IAsyncRelayCommand<bool> ShowCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class NoteImageViewModel(string path, IPictureDecoder pictures) : INoteImageViewModel, IDisposable
{
    public const int ThumbnailWidth = 240;

    public string Path { get; } = path;

    public static bool Shows(NoteMedia media) => media.MediaType.StartsWith("image/", StringComparison.Ordinal);

    public string Name { get; } = System.IO.Path.GetFileName(path);

    [ObservableProperty]
    public partial IPicture Picture { get; private set; } = NoPicture.Instance;

    public void Dispose() => Release();

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
            var encoded = await File.ReadAllBytesAsync(Path, cancellationToken);
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
