using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using static Avala.Host.Tests.DogfoodSettings;

namespace Avala.Host.Tests;

internal sealed class DogfoodCamera(DogfoodJournal journal, string screens)
{
    private readonly HashSet<string> shotOnce = [];
    private int shots;

    public Window Window { private get; set; } = null!;

    public async Task ShootOnceAsync(string key, string name)
    {
        if (shotOnce.Add(key))
        {
            await ShootAsync(name);
        }
    }

    public async Task ShootAsync(string name)
    {
        OverlayPopups();
        var file = Path.Combine(screens, $"{++shots:D2}-{name}");
        var (frame, texts) = Steady();
        frame?.Save(file + ".png", new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        await File.WriteAllLinesAsync(file + ".txt", texts, Cancellation);
        await journal.NoteAsync($"Screenshot {file}.png");
    }

    private (Avalonia.Media.Imaging.WriteableBitmap? Frame, List<string> Texts) Steady()
    {
        var attempts = 0;

        while (true)
        {
            Pump();
            var before = Texts();
            var frame = Window.CaptureRenderedFrame();
            var after = Texts();

            if (before.SequenceEqual(after, StringComparer.Ordinal) || ++attempts == 10)
            {
                return (frame, after);
            }
        }
    }

    private List<string> Texts() =>
        [
            .. Window.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text) && text.Bounds.Width > 0)
                .Select(text => text.Text!.ReplaceLineEndings(" ⏎ ")),
        ];

    public void OverlayPopups()
    {
        foreach (var popup in Window.GetLogicalDescendants().OfType<Popup>())
        {
            popup.ShouldUseOverlayLayer = true;
        }
    }

    private static void Pump()
    {
        for (var frame = 0; frame < 60; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
