using Avala.Shell;
using Avala.Testing.UI;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
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
        InWindowAsync(ui, run, variant, (window, drain) => ShootHostedAsync(window, name, arrange, drain));

    public static Task InWindowAsync(HeadlessUi ui, SimulatedRun run, ThemeVariant variant, Func<Window, Action, Task> body) =>
        ui.RunAsync(() => run.Ui.HostAsync(drain => InHostedWindowAsync(run, variant, body, drain)), TestContext.Current.CancellationToken);

    public static async Task SettleAsync(Window window, Action drain)
    {
        await ViewScript.DrawnAsync(window);
        Pump(drain);
    }

    private async Task ShootHostedAsync(Window window, string name, Action<Window>[] arrange, Action drain)
    {
        foreach (var step in arrange)
        {
            await ViewScript.DrawnAsync(window);
            step(window);
            Pump(drain);
        }

        await SettleAsync(window, drain);

        window.CaptureRenderedFrame()?.Save(Path.Combine(Directory.CreateDirectory(folder).FullName, name + ".png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        var texts = Directory.CreateDirectory(Path.Combine(folder, "texts")).FullName;
        await File.WriteAllLinesAsync(Path.Combine(texts, name + ".txt"), Texts(window), TestContext.Current.CancellationToken);
    }

    private static async Task InHostedWindowAsync(SimulatedRun run, ThemeVariant variant, Func<Window, Action, Task> body, Action drain)
    {
        Application.Current!.DataTemplates.Add(run.Views);
        var window = new ShellView { Width = 1440, Height = 900, RequestedThemeVariant = variant };
        window.Styles.Add(new Style(selector => selector.OfType<Popup>()) { Setters = { new Setter(Popup.ShouldUseOverlayLayerProperty, true) } });
        Avala.Components.UI.Theme.Motion.SetIsReduced(window, true);
        Avala.Components.UI.Elapsed.ElapsedText.SetClock(window, run.Clock);
        window.DataContext = run.Get<ShellViewModel>();

        try
        {
            window.Show();
            Pump(drain);
            await body(window, drain);
        }
        finally
        {
            foreach (var element in window.GetLogicalDescendants().OfType<StyledElement>().Reverse().ToList())
            {
                element.DataContext = null;
            }

            window.DataContext = null;
            Pump(drain);
            window.Close();
            Application.Current.DataTemplates.Remove(run.Views);
        }
    }

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

    public static void Pump(Action drain)
    {
        for (var frame = 0; frame < 60; frame++)
        {
            drain();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }
}
