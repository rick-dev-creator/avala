using Avala.Sdk;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace Avala.Components.UI.States;

public sealed class Dragging
{
    public const double Threshold = 6;

    public static readonly AttachedProperty<string> FileProperty =
        AvaloniaProperty.RegisterAttached<Dragging, Control, string>("File", string.Empty);

    public static readonly AttachedProperty<string> LinkProperty =
        AvaloniaProperty.RegisterAttached<Dragging, Control, string>("Link", string.Empty);

    public static readonly AttachedProperty<string> TextProperty =
        AvaloniaProperty.RegisterAttached<Dragging, Control, string>("Text", string.Empty);

    private static readonly AttachedProperty<bool> WatchedProperty =
        AvaloniaProperty.RegisterAttached<Dragging, Control, bool>("Watched");

    static Dragging()
    {
        FileProperty.Changed.AddClassHandler<Control>((control, _) => Watch(control));
        LinkProperty.Changed.AddClassHandler<Control>((control, _) => Watch(control));
        TextProperty.Changed.AddClassHandler<Control>((control, _) => Watch(control));
    }

    private Dragging()
    {
    }

    internal static Func<PointerPressedEventArgs, DataTransfer, Task> Starter { get; set; } =
        async (pressed, transfer) => _ = await DragDrop.DoDragDropAsync(pressed, transfer, DragDropEffects.Copy);

    public static string GetFile(Control control) => control.GetValue(FileProperty);

    public static void SetFile(Control control, string value) => control.SetValue(FileProperty, value);

    public static string GetLink(Control control) => control.GetValue(LinkProperty);

    public static void SetLink(Control control, string value) => control.SetValue(LinkProperty, value);

    public static string GetText(Control control) => control.GetValue(TextProperty);

    public static void SetText(Control control, string value) => control.SetValue(TextProperty, value);

    internal static void Follow(Control control, Func<PointerPressedEventArgs, Option<DragPayload>> payload, RoutingStrategies routing) =>
        new DragWatch(payload).Attach(control, routing);

    internal static async Task<DataTransfer> TransferAsync(Visual from, DragPayload payload)
    {
        var top = TopLevel.GetTopLevel(from);

        var item = new DataTransferItem();
        var transfer = new DataTransfer();

        if (payload.File.Length > 0 && top is not null && await top.StorageProvider.TryGetFileFromPathAsync(payload.File) is { } file)
        {
            item.SetFile(file);

            if (Decoded(payload.File) is { } bitmap)
            {
                item.SetBitmap(bitmap);
            }
        }

        if (payload.Text.Length > 0)
        {
            item.SetText(payload.Text);
        }

        transfer.Add(item);

        return transfer;
    }

    private static void Watch(Control control)
    {
        if (control.GetValue(WatchedProperty))
        {
            return;
        }

        control.SetValue(WatchedProperty, true);
        Follow(
            control,
            _ => DragPayload.Of(GetFile(control), GetLink(control), GetText(control)),
            RoutingStrategies.Bubble);
    }

    private static Bitmap? Decoded(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);

            return new Bitmap(stream);
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

internal sealed record DragPayload(string File, string Text)
{
    public static Option<DragPayload> Of(string file, string link, string text)
    {
        var said = file.Length > 0 && text.Length == 0 && link.Length == 0 ? new Uri(Path.GetFullPath(file)).AbsoluteUri : link.Length > 0 ? link : text;

        return file.Length == 0 && said.Length == 0 ? Option<DragPayload>.None : new DragPayload(file, said);
    }
}

internal sealed class DragWatch(Func<PointerPressedEventArgs, Option<DragPayload>> payload)
{
    private PointerPressedEventArgs? pressed;
    private DragPayload? carrying;
    private Point origin;

    public void Attach(Control control, RoutingStrategies routing)
    {
        control.AddHandler(InputElement.PointerPressedEvent, (sender, e) => Press(control, e), routing, handledEventsToo: true);
        control.AddHandler(InputElement.PointerMovedEvent, (sender, e) => Move(control, e), RoutingStrategies.Bubble, handledEventsToo: true);
        control.AddHandler(InputElement.PointerReleasedEvent, (sender, e) => Release(), RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private static async Task BeginAsync(Visual from, PointerPressedEventArgs started, DragPayload kept) =>
        await Dragging.Starter(started, await Dragging.TransferAsync(from, kept));

    private void Press(Control control, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(control).Properties.IsLeftButtonPressed && payload(e).Match<DragPayload?>(found => found, () => null) is { } found)
        {
            pressed = e;
            carrying = found;
            origin = e.GetPosition(control);
        }
    }

    private void Move(Control control, PointerEventArgs e)
    {
        if (pressed is not { } started || carrying is not { } kept)
        {
            return;
        }

        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            Release();

            return;
        }

        var at = e.GetPosition(control);

        if (Math.Abs(at.X - origin.X) < Dragging.Threshold && Math.Abs(at.Y - origin.Y) < Dragging.Threshold)
        {
            return;
        }

        Release();
        _ = BeginAsync(control, started, kept);
    }

    private void Release()
    {
        pressed = null;
        carrying = null;
    }
}
