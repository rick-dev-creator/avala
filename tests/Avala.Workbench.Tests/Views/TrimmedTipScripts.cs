using Avala.Testing.UI;
using Avala.Workbench.Resources;
using Avala.Workbench.Triggers;
using Avala.Workbench.Usage;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Avala.Workbench.Tests.Views;

public sealed class TrimmedTipScripts(HeadlessUi ui)
{
    [Fact]
    public Task AConnectionsProviderCutByANarrowMeterOffersItselfWholeAsync() =>
        ui.RunAsync(() => OffersItselfWhole(Screen.Show(new DesignConnectionMeterViewModel(), ThemeVariant.Dark, 90, 400).Find<TextBlock>("Provider")), TestContext.Current.CancellationToken);

    [Fact]
    public Task TheLeasedPortsCutByANarrowCardOfferThemselvesWholeAsync() =>
        ui.RunAsync(
            () => OffersItselfWhole(Screen.Show(new DesignResourcesViewModel(), ThemeVariant.Dark, 520, 1400).All<TextBlock>().First(text => text.Name == "Ports" && Avala.Components.UI.Trimming.TrimmedTip.GetIsOffered(text))),
            TestContext.Current.CancellationToken);

    [Fact]
    public Task ALoopIterationCutByANarrowRowOffersItselfWholeAsync() =>
        ui.RunAsync(() => OffersItselfWhole(Lines(Screen.Show(new DesignLoopItemViewModel(), ThemeVariant.Dark, 260, 400), "LoopIterations")[0]), TestContext.Current.CancellationToken);

    [Fact]
    public Task AForgeEventCutByANarrowPageOffersItselfWholeAsync() =>
        ui.RunAsync(() => OffersItselfWhole(Lines(Screen.Show(new DesignTriggersViewModel(), ThemeVariant.Dark, 320, 1400), "ForgeEvents")[0]), TestContext.Current.CancellationToken);

    [Fact]
    public Task AWebhookDeliveryCutByANarrowPageOffersItselfWholeAsync() =>
        ui.RunAsync(() => OffersItselfWhole(Lines(Screen.Show(new DesignTriggersViewModel(), ThemeVariant.Dark, 320, 1400), "Deliveries")[0]), TestContext.Current.CancellationToken);

    private static IReadOnlyList<TextBlock> Lines(ViewScript view, string list) =>
        [.. view.Find<ItemsControl>(list).GetVisualDescendants().OfType<TextBlock>()];

    private static void OffersItselfWhole(TextBlock text) =>
        Assert.Equal((true, text.Text), (text.TextLayout.TextLines.Any(line => line.HasCollapsed), ToolTip.GetTip(text)));
}
