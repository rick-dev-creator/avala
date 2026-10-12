using Avala.Jobs.Contracts;
using Avala.Testing.UI;
using Avala.Workbench.RepositoryChoice;
using Avala.Workbench.Tests.RepositoryChoice;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class RepositoryPickerViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheRepositoryIsOnePopUpButtonWhoseMenuOffersNoRepositoryTheRecentOnesAndTheWaysToGetAnotherAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignRepositoryPickerViewModel(), Avalonia.Styling.ThemeVariant.Dark, 720, 640).Settle();

            view.Click("RepositoryButton");

            Assert.Equal(("shop-api", "/home/ada/code"), (view.TextOf("RepositoryTitle"), view.TextOf("RepositoryDetail")));
            Assert.Equal((true, true, true, true), (view.Shows("Scratch"), view.Shows("OpenFolder"), view.Shows("Clone"), view.Shows("NewRepository")));
            Assert.Equal(["shop-api", "shop-web", "ledger"], view.All<TextBlock>().Where(text => text.Name == "ChoiceTitle").Select(text => text.Text));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ChoosingARecentRepositoryInTheMenuShowsItOnTheButtonAsync() =>
        ui.RunAsync(() =>
        {
            var picker = Choosing.Picker(Pages.Board(
                Pages.Summary("Fix the failing test", JobStatus.Running) with { Repository = "/home/ada/code/ledger", Submitted = DateTimeOffset.UnixEpoch },
                Pages.Summary("Add an endpoint", JobStatus.Running) with { Repository = "/home/ada/code/shop-api", Submitted = DateTimeOffset.UnixEpoch.AddHours(1) }));
            picker.ShowKnown();
            var view = Screen.Show(picker, Avalonia.Styling.ThemeVariant.Dark, 720, 640).Settle();

            view.Click("RepositoryButton");
            view.Click(view.All<Button>().Last(button => button.Name == "Choice"));

            Assert.Equal(("ledger", false, false), (view.TextOf("RepositoryTitle"), picker.IsOpen, view.Find<Avalonia.Controls.Primitives.Popup>("RepositoryMenu").IsOpen));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task NewRepositoryAsksForAParentThenANameAndShowsItOnTheButtonAsync() =>
        ui.RunAsync(async () =>
        {
            var picker = Choosing.Picker(new Avala.Workbench.Board.JobBoard(), new FakeFolderPicker { Answer = "/home/ada/code" }, new FakeRepositories());
            var field = picker.Chooser;
            var view = Screen.Show(picker, Avalonia.Styling.ThemeVariant.Dark, 720, 640).Settle();

            view.Click("RepositoryButton");
            view.Click("NewRepository");
            await (field.Creation.StartCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();
            var asked = (view.Shows("NewRepositoryName"), view.TextOf("ParentFolder"));
            view.Type("NewRepositoryName", "invoices").Click("Create");
            await (field.Creation.CreateCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal((true, "In /home/ada/code"), asked);
            Assert.Equal(("invoices", false), (view.TextOf("RepositoryTitle"), view.Shows("NewRepositoryName")));
        }, TestContext.Current.CancellationToken);
}

public sealed class RepositoryChoiceViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AChoiceShowsItsFolderNameAndWhereItLivesAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignRepositoryChoiceViewModel());

            Assert.Equal(("shop-api", "/home/ada/code", true), (view.TextOf("ChoiceTitle"), view.TextOf("ChoiceDetail"), view.HasClass("Choice", "selected")));
        }, TestContext.Current.CancellationToken);
}
