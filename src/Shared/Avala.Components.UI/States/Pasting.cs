using System.Windows.Input;
using Avala.Components.Attaching;
using Avala.Components.UI.Markdown;
using Avala.Sdk;
using AvaloniaClipboard = Avalonia.Input.Platform.IClipboard;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Avala.Components.UI.States;

public sealed class Pasting
{
    public static readonly AttachedProperty<ICommand> CommandProperty =
        AvaloniaProperty.RegisterAttached<Pasting, TextBox, ICommand>("Command", OpensNothing.Instance);

    static Pasting() => CommandProperty.Changed.AddClassHandler<TextBox>((box, change) =>
    {
        if (change.GetOldValue<ICommand>() == OpensNothing.Instance)
        {
            box.PastingFromClipboard += OnPasting;
        }
    });

    private Pasting()
    {
    }

    public static ICommand GetCommand(TextBox box) => box.GetValue(CommandProperty);

    public static void SetCommand(TextBox box, ICommand value) => box.SetValue(CommandProperty, value);

    private static void OnPasting(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || TopLevel.GetTopLevel(box)?.Clipboard is not { } clipboard)
        {
            return;
        }

        e.Handled = true;
        _ = PasteAsync(box, clipboard);
    }

    private static async Task PasteAsync(TextBox box, AvaloniaClipboard clipboard)
    {
        var command = GetCommand(box);
        var files = (await clipboard.TryGetFilesAsync() ?? []).Select(file => file.TryGetLocalPath()).OfType<string>().ToList();

        if (files.Count > 0)
        {
            command.Execute(new Pasted(files, Option<byte[]>.None, string.Empty));
            return;
        }

        if (await clipboard.TryGetBitmapAsync() is { } bitmap)
        {
            using var encoded = new MemoryStream();
            bitmap.Save(encoded, new PngBitmapEncoderOptions());
            command.Execute(new Pasted([], encoded.ToArray(), string.Empty));
            return;
        }

        var text = await clipboard.TryGetTextAsync() ?? string.Empty;

        if (text.Length > Pasted.LongText)
        {
            command.Execute(new Pasted([], Option<byte[]>.None, text));
            return;
        }

        box.SelectedText = text;
    }
}
