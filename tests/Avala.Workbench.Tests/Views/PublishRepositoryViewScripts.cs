using Avala.Testing.UI;
using Avala.Workbench.Inspector;
using Avala.Workbench.RepositoryChoice;
using Avala.Workbench.Tests.RepositoryChoice;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

public sealed class PublishRepositoryViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task PublishRepositoryOpensTheForgeChoicesAndPublishSaysWhereItWentAsync() =>
        ui.RunAsync(async () =>
        {
            var publishing = new FakePublishing();
            var publisher = new PublishRepositoryViewModel(publishing, new PublishOptionsViewModel(publishing));
            publisher.Show("/home/ada/code/shop");
            await publisher.Reading;
            var view = Screen.Show(publisher).Settle();
            var offered = (view.TextOf("Remote"), view.Shows("PublishRepository"), view.Shows("PublishName"));

            view.Click("PublishRepository");
            await (publisher.StartCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();
            var chosen = (view.Find<ComboBox>("PublishTarget").SelectedItem as string, view.Find<TextBox>("PublishName").Text, view.Find<RadioButton>("PublishPrivate").IsChecked);
            view.Click("Publish");
            await (publisher.PublishCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal((PublishPhrases.NoRemote, true, false), offered);
            Assert.Equal(("github", "shop", (bool?)true), chosen);
            Assert.Equal(("origin is ada/shop on github (GitHub).", false, false), (view.TextOf("Remote"), view.Shows("PublishRepository"), view.Shows("PublishName")));
            Assert.StartsWith("Published ada/shop on github", view.TextOf("PublishNotice"), StringComparison.Ordinal);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task WithoutAForgeThatCreatesRepositoriesThePanelSaysHowToAddOneAsync() =>
        ui.RunAsync(async () =>
        {
            var publishing = new FakePublishing();
            publishing.Targets.Clear();
            var publisher = new PublishRepositoryViewModel(publishing, new PublishOptionsViewModel(publishing));
            publisher.Show("/home/ada/code/shop");
            await publisher.Reading;
            var view = Screen.Show(publisher).Settle();

            view.Click("PublishRepository");
            await (publisher.StartCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal((PublishPhrases.NoTargets, false, false), (view.TextOf("PublishProblem"), view.Shows("PublishTarget"), view.Find<Button>("Publish").IsEffectivelyEnabled));
        }, TestContext.Current.CancellationToken);
}
