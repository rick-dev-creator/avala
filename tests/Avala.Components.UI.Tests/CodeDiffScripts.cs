using Avala.Components.UI.Markdown;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Avala.Components.UI.Tests;

public sealed class CodeDiffScripts(HeadlessUi ui)
{
    [Fact]
    public Task ADiffColorsTheCodeBeforeAndAfterTheChangeEachOnItsOwnAndKeepsItsMarksAsync() =>
        ui.RunAsync(async () =>
        {
            var diff = new CodeDiff { Diff = "-/* totals\n+// totals\n var sum = \"0\";\n… 3 more lines", Language = "cs" };
            var window = new Window { Width = 640, Height = 480, RequestedThemeVariant = ThemeVariant.Light, Content = diff };
            window.Show();

            await CodeBlocks.PaintedAsync(diff);
            var lines = diff.Lines;

            Assert.Equal([(true, false), (false, true), (false, false), (false, false)], lines.Select(line => (line.Classes.Contains("removed"), line.Classes.Contains("added"))));
            Assert.Equal((Color.Parse("#008000"), Color.Parse("#008000")), (CodeBlocks.ColorOf(lines[0], "totals"), CodeBlocks.ColorOf(lines[1], "totals")));
            Assert.Equal((Colors.Blue, Color.Parse("#A31515")), (CodeBlocks.ColorOf(lines[2], "var"), CodeBlocks.ColorOf(lines[2], "0")));
            Assert.Equal(["-/* totals", "+// totals", " var sum = \"0\";", "… 3 more lines"], lines.Select(line => line.Text));
        }, TestContext.Current.CancellationToken);
}
