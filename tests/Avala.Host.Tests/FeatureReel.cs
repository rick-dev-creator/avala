using Avala.Components.UI.Markdown;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Avala.Host.Tests;

internal sealed record ReelFrame(Action<Window> Step, int Hold = 1);

internal sealed class FeatureReel(HeadlessUi ui, string folder)
{
    public const int FramesPerSecond = 12;

    private static readonly (ThemeVariant Variant, string Name)[] Themes = [(ThemeVariant.Dark, "dark"), (ThemeVariant.Light, "light")];

    public static ReelFrame Hold(double seconds) => new(_ => { }, Frames(seconds));

    public static int Frames(double seconds) => Math.Max(1, (int)Math.Round(seconds * FramesPerSecond));

    public async Task FilmAsync(SimulatedRun run, string reel, params ReelFrame[] frames)
    {
        foreach (var (variant, theme) in Themes)
        {
            var target = Directory.CreateDirectory(Path.Combine(folder, "reels", $"{reel}-{theme}")).FullName;
            await FeatureCamera.InWindowAsync(ui, run, variant, (window, drain) => FilmHostedAsync(window, drain, target, frames));
        }
    }

    private static async Task FilmHostedAsync(Window window, Action drain, string target, IReadOnlyList<ReelFrame> frames)
    {
        foreach (var frame in frames)
        {
            await FeatureCamera.SettleAsync(window, drain);
            frame.Step(window);
            FeatureCamera.Pump(drain);
            await FeatureCamera.SettleAsync(window, drain);
            await Task.WhenAll(window.GetVisualDescendants().OfType<CodeText>().Where(code => code.IsEffectivelyVisible).Select(CodeBlocks.PaintedAsync));
            FeatureCamera.Pump(drain);

            var first = Next(target);
            window.CaptureRenderedFrame()!.Save(first, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

            for (var copy = 1; copy < frame.Hold; copy++)
            {
                File.Copy(first, Next(target));
            }
        }
    }

    private static string Next(string target) =>
        Path.Combine(target, $"{Directory.GetFiles(target, "*.png").Length:D4}.png");
}
