using Avala.Shell;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avala.Host.Tests;

internal sealed class FeatureCamera(HeadlessUi ui, string folder)
{
    public const string Gate = "AVALA_FEATURE_SCREENS";

    public static string? Folder => Environment.GetEnvironmentVariable(Gate) is { Length: > 0 } found ? found : null;

    public Task ShootAsync(SimulatedRun run, string name, params Action<Window>[] arrange) =>
        ShootAsync(run, name, ThemeVariant.Dark, arrange);

    public Task ShootAsync(SimulatedRun run, string name, ThemeVariant variant, params Action<Window>[] arrange) =>
        ui.RunAsync(
            async () =>
            {
                Application.Current!.DataTemplates.Add(run.Views);
                var window = new ShellView { Width = 1440, Height = 900, RequestedThemeVariant = variant };
                window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
                Avala.Components.UI.Theme.Motion.SetIsReduced(window, true);
                window.DataContext = run.Get<ShellViewModel>();

                try
                {
                    window.Show();
                    Pump();

                    foreach (var step in arrange)
                    {
                        step(window);
                        Pump();
                    }

                    window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    var texts = Directory.CreateDirectory(Path.Combine(folder, "texts")).FullName;
                    await File.WriteAllLinesAsync(Path.Combine(texts, name + ".txt"), Texts(window), TestContext.Current.CancellationToken);
                }
                finally
                {
                    window.DataContext = null;
                    Pump();
                    window.Close();
                    Application.Current.DataTemplates.Remove(run.Views);
                }
            },
            TestContext.Current.CancellationToken);

    public static void ScrollTo(Window window, string text)
    {
        var target = window.GetVisualDescendants().OfType<TextBlock>().LastOrDefault(block => block.Text?.Contains(text, StringComparison.Ordinal) == true);
        target?.BringIntoView();
    }

    public static void ScrollToEnd(Window window, string name)
    {
        foreach (var scroller in window.GetVisualDescendants().OfType<Control>().Where(control => control.Name == name).SelectMany(control => control.GetVisualDescendants().OfType<ScrollViewer>().Take(1)))
        {
            scroller.ScrollToEnd();
        }
    }

    public static Action<Window> Unfold(params string[] headers) =>
        window =>
        {
            foreach (var fold in window.GetVisualDescendants().OfType<Avala.Components.UI.Fold>().ToList())
            {
                fold.IsExpanded = headers.Contains(fold.Header as string);
            }
        };

    public static Action<Window> Pick(string name, string item) =>
        window =>
        {
            foreach (var box in window.GetVisualDescendants().OfType<ComboBox>().Where(box => box.Name == name).ToList())
            {
                box.SelectedItem = item;
            }
        };

    public static Action<Window> Open(string name) =>
        window =>
        {
            foreach (var box in window.GetVisualDescendants().OfType<ComboBox>().Where(box => box.Name == name).ToList())
            {
                box.IsDropDownOpen = true;
            }
        };

    private static List<string> Texts(Window window) =>
        [
            .. window.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text) && text.Bounds.Width > 0)
                .Select(text => text.Text!.ReplaceLineEndings(" ⏎ ")),
        ];

    private static void Pump()
    {
        for (var frame = 0; frame < 60; frame++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
