using System.Collections.ObjectModel;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Sessions;
using Avala.Components.Attaching;
using Avala.Components.Pictures;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IAttachmentTrayViewModel
{
    IReadOnlyList<IAttachmentChipViewModel> Chips { get; }

    bool HasAttachments { get; }

    bool CanAttach { get; }

    string Problem { get; }

    bool HasProblem { get; }

    IAsyncRelayCommand AttachCommand { get; }

    IAsyncRelayCommand<IReadOnlyList<string>> DropCommand { get; }

    IAsyncRelayCommand<Pasted> PasteCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class AttachmentTrayViewModel(IJobAttachments store, IFilePicker picker, IPictureDecoder pictures) : IAttachmentTrayViewModel, IDisposable
{
    public const string PastedImage = "pasted-image.png";

    public const string PastedText = "pasted-text.txt";

    private readonly ObservableCollection<AttachmentChipViewModel> chips = [];
    private Option<AcceptsAttachments> accepted;

    public IReadOnlyList<IAttachmentChipViewModel> Chips => chips;

    public IReadOnlyList<Attachment> Attached => [.. chips.Select(chip => chip.Attachment)];

    [ObservableProperty]
    public partial bool HasAttachments { get; private set; }

    [ObservableProperty]
    public partial bool CanAttach { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string Problem { get; private set; } = string.Empty;

    public bool HasProblem => Problem.Length > 0;

    public void Accept(Option<AcceptsAttachments> types)
    {
        accepted = types;
        CanAttach = types.IsSome;
    }

    public void Clear()
    {
        foreach (var chip in chips)
        {
            chip.Dispose();
        }

        chips.Clear();
        HasAttachments = false;
        Problem = string.Empty;
    }

    public void Dispose() => Clear();

    [RelayCommand]
    private async Task AttachAsync(CancellationToken cancellationToken) =>
        await KeepFilesAsync(await picker.PickFilesAsync(AttachmentPhrases.Title, cancellationToken), cancellationToken);

    [RelayCommand]
    private Task DropAsync(IReadOnlyList<string>? paths, CancellationToken cancellationToken) =>
        KeepFilesAsync(paths ?? [], cancellationToken);

    [RelayCommand]
    private async Task PasteAsync(Pasted? pasted, CancellationToken cancellationToken)
    {
        if (pasted is null)
        {
            return;
        }

        if (pasted.Files.Count > 0)
        {
            await KeepFilesAsync(pasted.Files, cancellationToken);
            return;
        }

        var (name, content) = pasted.Image.Match(image => (PastedImage, image), () => (PastedText, System.Text.Encoding.UTF8.GetBytes(pasted.Text)));

        if (Accepts(name))
        {
            using var stream = new MemoryStream(content);
            Add(name, await store.KeepAsync(name, stream, cancellationToken));
        }
    }

    private async Task KeepFilesAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        Problem = string.Empty;

        foreach (var path in paths.Where(path => Accepts(Path.GetFileName(path))))
        {
            Add(Path.GetFileName(path), await store.KeepFileAsync(path, cancellationToken));
        }
    }

    private bool Accepts(string name)
    {
        if (accepted.Match(types => types.Accepts(store.MediaTypeOf(name)), () => false))
        {
            return true;
        }

        Problem = AttachmentPhrases.Unaccepted(name);

        return false;
    }

    private void Add(string name, Result<Attachment, AttachmentRefusal> kept)
    {
        if (!kept.TryGetValue(out var attachment, out var refusal))
        {
            Problem = AttachmentPhrases.Refused(name, refusal);
            return;
        }

        var chip = new AttachmentChipViewModel(attachment, pictures, removable: true);
        chip.Removed += (_, _) => Remove(chip);
        chips.Add(chip);
        HasAttachments = true;
    }

    private void Remove(AttachmentChipViewModel chip)
    {
        chips.Remove(chip);
        chip.Dispose();
        HasAttachments = chips.Count > 0;
    }
}

internal static class AttachmentPhrases
{
    public const string Title = "Attach files";

    public static string Refused(string name, AttachmentRefusal refusal) => refusal switch
    {
        AttachmentRefusal.TooLarge => $"{name} is larger than 20 MB.",
        AttachmentRefusal.Unreadable => $"{name} could not be read.",
        _ => $"{name} could not be kept.",
    };

    public static string Unaccepted(string name) => $"This connection does not take {name}.";
}
