using Avala.Agents.Contracts.Sessions;
using Avala.Components.Pictures;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IMediaViewerViewModel
{
    bool IsOpen { get; }

    string Name { get; }

    string Caption { get; }

    bool HasCaption { get; }

    string Position { get; }

    bool HasMany { get; }

    IPicture Picture { get; }

    IAsyncRelayCommand PreviousCommand { get; }

    IAsyncRelayCommand NextCommand { get; }

    IAsyncRelayCommand CloseCommand { get; }

    IAsyncRelayCommand<int> FitCommand { get; }
}

internal sealed record ShownImage(Attachment Media, string Caption);

[INotifyPropertyChanged]
internal sealed partial class MediaViewerViewModel(IPictureDecoder pictures) : IMediaViewerViewModel
{
    private IReadOnlyList<ShownImage> shown = [];
    private int index;
    private int fitted;
    private int shownWidth;
    private int asked;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand), nameof(PreviousCommand), nameof(NextCommand))]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCaption))]
    public partial string Caption { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Position { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasMany { get; private set; }

    [ObservableProperty]
    public partial IPicture Picture { get; private set; } = NoPicture.Instance;

    public bool HasCaption => Caption.Length > 0;

    public void Show(IReadOnlyList<ShownImage> images, ShownImage chosen)
    {
        shown = images;
        IsOpen = shown.Count > 0;
        StepCommand.Execute(Math.Max(0, IndexOf(chosen)));
    }

    private bool CanStep => IsOpen && shown.Count > 1;

    [RelayCommand(CanExecute = nameof(CanStep))]
    private Task PreviousAsync() => StepAsync((index + shown.Count - 1) % shown.Count);

    [RelayCommand(CanExecute = nameof(CanStep))]
    private Task NextAsync() => StepAsync((index + 1) % shown.Count);

    [RelayCommand(CanExecute = nameof(IsOpen), AllowConcurrentExecutions = true)]
    private Task CloseAsync()
    {
        IsOpen = false;
        asked++;
        shownWidth = 0;
        Release(NoPicture.Instance);

        return Task.CompletedTask;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task FitAsync(int width)
    {
        fitted = width;

        return IsOpen && Wanted > shownWidth ? DecodeAsync() : Task.CompletedTask;
    }

    private int Wanted => Math.Max(fitted, MediaViewModel.PictureWidth);

    private int IndexOf(ShownImage chosen)
    {
        for (var at = 0; at < shown.Count; at++)
        {
            if (ReferenceEquals(shown[at], chosen))
            {
                return at;
            }
        }

        return -1;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task StepAsync(int at)
    {
        if (shown.Count == 0)
        {
            return Task.CompletedTask;
        }

        index = at;
        var current = shown[index];
        Name = current.Media.Name;
        Caption = current.Caption;
        HasMany = shown.Count > 1;
        Position = MediaPhrases.Position(index, shown.Count);
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
        shownWidth = 0;

        return DecodeAsync();
    }

    private async Task DecodeAsync()
    {
        var asking = ++asked;
        var width = Wanted;
        var picture = await ReadAsync(shown[index].Media.Path, width);

        if (asking != asked)
        {
            picture.Dispose();

            return;
        }

        shownWidth = picture.IsNothing ? 0 : width;
        Release(picture);
    }

    private async Task<IPicture> ReadAsync(string path, int width)
    {
        try
        {
            var encoded = await File.ReadAllBytesAsync(path, CancellationToken.None);

            return await pictures.DecodeAsync(encoded, width, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return NoPicture.Instance;
        }
    }

    private void Release(IPicture next)
    {
        var previous = Picture;
        Picture = next;
        previous.Dispose();
    }
}
