using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Testing.UI;
using Avala.Workbench.Machine;
using Avala.Workbench.RepositoryRules;
using Avala.Workbench.Settings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Avala.Workbench.Tests.Views;

public sealed class SettingsViewScripts(HeadlessUi ui)
{
    private const string Repository = "/repositories/shop";

    [Fact]
    public Task TheRepositoriesAreListedBesideTheirRulesWithTheShownOneSelectedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new DesignSettingsViewModel());

            Assert.Equal(4, view.Find<ItemsControl>("Repositories").ItemCount);
            Assert.Contains("ledger-api", view.VisibleTexts);
            Assert.Contains(view.All<UserControl>(), control => control.GetType().Name == "RepositorySettingsView");
            Assert.Contains(view.All<UserControl>(), control => control.GetType().Name == "MachineSettingsView");
            Assert.Contains(view.All<UserControl>(), control => control.GetType().Name == "AppearanceView");
            Assert.False(view.Shows("NoRepositories"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task TypingARepositoryAndPressingEnterReadsItsRulesAsync() =>
        ui.RunAsync(async () =>
        {
            var settings = new SettingsViewModel(Repositories(new FakeOpener()), new DesignMachineSettingsViewModel(), new DesignAppearanceViewModel(), new DesignAboutViewModel());
            var view = Wide(settings);
            var before = view.Shows("Rules");

            view.Type("RepositoryPath", Repository);
            view.Press(Key.Enter);
            await (settings.Repository.ReadCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal((false, true, true), (before, view.Shows("Rules"), view.Shows("NoRepositories")));
            Assert.Equal("shop", view.TextOf("RepositoryName"));
        }, TestContext.Current.CancellationToken);

    internal static RepositorySettingsViewModel Repositories(FakeOpener opener) => Repositories(opener, new FakeWorkingFiles());

    internal static RepositorySettingsViewModel Repositories(FakeOpener opener, FakeWorkingFiles working)
    {
        var rules = new FakeRules();

        return new RepositorySettingsViewModel(
            new RulesReader(rules, rules, rules, new CommittedFiles().Workspace(Repository)),
            new SettingsFiles(opener, new AvalaPaths("/data")),
            Pages.Board(),
            new RuleFileEditorViewModel(new RuleFileEditing(working, [new BudgetLikeFormat()])));
    }

    private static ViewScript Wide(object viewModel)
    {
        var view = Screen.Show(viewModel);
        view.Window.Width = 1440;
        view.Window.Height = 1000;

        return view.Settle();
    }
}

public sealed class RepositorySettingsViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheRulesAreShownInDecisionOrderWithTheirOriginAndAnswerColorsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignRepositorySettingsViewModel());
            var rules = view.All<UserControl>().Where(control => control.GetType().Name == "RuleView").ToList();

            Assert.Equal((4, 6, 1, 4), (view.Find<ItemsControl>("Files").ItemCount, view.Find<ItemsControl>("RuleList").ItemCount, view.Find<ItemsControl>("Caps").ItemCount, view.Find<ItemsControl>("Checks").ItemCount));
            Assert.Equal(("ledger-api", "Autonomous", "Recommended options"), (view.TextOf("RepositoryName"), view.TextOf("Autonomy"), view.TextOf("FormStrategy")));
            Assert.Equal(["Deny", "Ask", "Allow", "Deny", "Allow", "Allow"], rules.Select(rule => rule.Classes.Contains("deny") ? "Deny" : rule.Classes.Contains("ask") ? "Ask" : "Allow"));
            Assert.Equal((false, false, false), (view.Shows("JobsFile"), view.Shows("Error"), view.Shows("NothingRead")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task EditInRepositoryOpensTheFileTheSectionComesFromAsync() =>
        ui.RunAsync(async () =>
        {
            var opener = new FakeOpener();
            var settings = SettingsViewScripts.Repositories(opener);
            await settings.OpenCommand.ExecuteAsync("/repositories/shop");
            var view = Screen.Show(settings);
            view.Window.Height = 1400;
            view.Settle();

            view.Click("EditAutonomy");
            await (settings.EditCommand.ExecutionTask ?? Task.CompletedTask);
            view.Click("EditBudget");
            await (settings.EditCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal([Path.Combine("/repositories/shop", ".avala/permissions.json"), Path.Combine("/repositories/shop", ".avala/budget.json")], opener.Opened);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task EditHereOpensTheEditorAboveTheRulesAndCancelClosesItAsync() =>
        ui.RunAsync(async () =>
        {
            var working = new FakeWorkingFiles();
            working.Files[".avala/budget.json"] = "{ \"holdAtLimit\": 0.9 }";
            var settings = SettingsViewScripts.Repositories(new FakeOpener(), working);
            await settings.OpenCommand.ExecuteAsync("/repositories/shop");
            var view = Screen.Show(settings);
            view.Window.Height = 1400;
            view.Settle();
            var before = view.Shows("Editor");

            view.Click("EditBudgetHere");
            await (settings.EditHereCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();
            var opened = (view.Shows("Editor"), view.Find<TextBox>("FileText").Text);
            view.Click("CancelFile");
            view.Settle();

            Assert.Equal((false, (true, "{ \"holdAtLimit\": 0.9 }"), false), (before, opened, view.Shows("Editor")));
            Assert.False(view.Find<Button>("EditAutonomyHere").IsEffectivelyEnabled);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task BeforeARepositoryIsReadNoRulesShowAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(SettingsViewScripts.Repositories(new FakeOpener()));

            Assert.Equal((false, true), (view.Shows("Rules"), view.Shows("NothingRead")));
        }, TestContext.Current.CancellationToken);
}

public sealed class MachineSettingsViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheMachineShowsItsConnectionsFilesAndSilenceWindowOnTheSliderAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignMachineSettingsViewModel());

            Assert.Equal(("connections.json: Applied", "10 min", 10d), (view.TextOf("ConnectionsFile"), view.TextOf("SilenceLabel"), view.Find<Slider>("SilenceSlider").Value));
            Assert.Equal((2, false), (view.Find<ItemsControl>("Connections").ItemCount, view.Shows("SaveSilence")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task MovingTheSliderSetsTheWindowInSecondsAndOffersToSaveItAsync() =>
        ui.RunAsync(() =>
        {
            var machine = new DesignMachineSettingsViewModel();
            var view = Screen.Show(machine);

            view.Find<Slider>("SilenceSlider").Value = 14.6;
            view.Settle();

            Assert.Equal(("900", "15 min", true), (machine.SilenceDraft, view.TextOf("SilenceLabel"), view.Shows("SaveSilence")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AnInvalidWindowLeavesTheSliderWhereItWasAndSaysSoAsync() =>
        ui.RunAsync(() =>
        {
            var machine = new DesignMachineSettingsViewModel();
            var view = Screen.Show(machine);

            view.Find<TextBox>("SilenceDraft").Text = "soon";
            view.Settle();

            Assert.Equal((10d, "not a number of seconds"), (view.Find<Slider>("SilenceSlider").Value, view.TextOf("SilenceLabel")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AWindowBeyondTheSliderRangeKeepsItsExactSecondsAsync() =>
        ui.RunAsync(() =>
        {
            var machine = new DesignMachineSettingsViewModel();
            var view = Screen.Show(machine);

            view.Find<TextBox>("SilenceDraft").Text = "7200";
            view.Settle();

            Assert.Equal((60d, "7200", "120 min"), (view.Find<Slider>("SilenceSlider").Value, machine.SilenceDraft, view.TextOf("SilenceLabel")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task PressingEnterInTheWindowSavesItAsync() =>
        ui.RunAsync(async () =>
        {
            var supervision = new FakeSupervision();
            var machine = Machine(new FakeConnections(), supervision);
            var view = Screen.Show(machine);

            view.Type("SilenceDraft", "120");
            view.Press(Key.Enter);
            await (machine.SaveSilenceCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal([TimeSpan.FromSeconds(120)], supervision.Changes);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARefusedWindowShowsWhyAsync() =>
        ui.RunAsync(async () =>
        {
            var machine = Machine(new FakeConnections(), new FakeSupervision());
            var view = Screen.Show(machine);

            view.Find<TextBox>("SilenceDraft").Text = "later";
            await machine.SaveSilenceCommand.ExecuteAsync(null);
            view.Settle();

            Assert.Equal((true, "Enter the window in seconds."), (view.Shows("Error"), view.TextOf("ErrorText")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task TheDefaultConnectionIsChosenAboveTheConnectionsAsync() =>
        ui.RunAsync(async () =>
        {
            var connections = new FakeConnections("claude-work", "claude-personal").Automatic();
            var machine = Machine(connections, new FakeSupervision());
            await machine.LoadAsync(TestContext.Current.CancellationToken);
            var view = Screen.Show(machine);

            Assert.Equal((DefaultPhrases.Auto, true, false), (view.Find<ComboBox>("Choices").SelectedItem, view.Shows("Recommended"), view.Shows("Save")));
            Assert.Equal([false, false], view.Find<ItemsControl>("Connections").Items.Cast<IMachineConnectionViewModel>().Select(connection => connection.IsDefault));
        }, TestContext.Current.CancellationToken);

    internal static MachineSettingsViewModel Machine(FakeConnections connections, FakeSupervision supervision)
    {
        var settings = new MachineSettings(connections, supervision, new FakeResources(), new FakeForgeCatalog());

        return new(
            settings,
            new SettingsFiles(new FakeOpener(), new AvalaPaths("/data")),
            new DefaultConnectionViewModel(settings, new CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger()),
            new ConnectionEditorViewModel(settings, new Avala.Workbench.ModelChoices.ModelPickerViewModel()));
    }

    [Fact]
    public Task AddingAConnectionOpensTheFormUnderTheListAndSavingClosesItAsync() =>
        ui.RunAsync(async () =>
        {
            var connections = new FakeConnections("claude-work");
            var machine = Machine(connections, new FakeSupervision());
            await machine.LoadAsync(TestContext.Current.CancellationToken);
            var view = Screen.Show(machine);

            view.Click("AddConnection");
            view.Settle();
            var opened = (view.Shows("Form"), view.Find<Button>("SaveConnection").IsEffectivelyEnabled);
            view.Type("NameField", "claude-team");
            view.Settle();
            await Assert.IsAssignableFrom<CommunityToolkit.Mvvm.Input.IAsyncRelayCommand>(view.Find<Button>("SaveConnection").Command).ExecuteAsync(null);
            view.Settle();

            Assert.Equal((true, false), opened);
            Assert.Equal(["declare claude-team simulator"], connections.Edits);
            Assert.Equal((false, 2), (view.Shows("Form"), view.Find<ItemsControl>("Connections").ItemCount));
            Assert.Equal("Added claude-team to connections.json. New jobs can run on it now.", view.TextOf("NoticeText"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ADeclaredConnectionOffersEditAndRemoveAndRemovingAsksFirstAsync() =>
        ui.RunAsync(async () =>
        {
            var connections = new FakeConnections("claude-work", "claude-personal");
            var machine = Machine(connections, new FakeSupervision());
            await machine.LoadAsync(TestContext.Current.CancellationToken);
            var view = Screen.Show(machine);

            var remove = view.All<Button>().Where(button => button.Name == "Remove").ToList();
            remove[1].Command!.Execute(null);
            view.Settle();

            Assert.Equal(2, view.All<Button>().Count(button => button.Name == "Edit" && button.IsEffectivelyVisible));
            Assert.Equal((true, "Remove claude-personal from connections.json? Jobs already running on it go on."), (view.Shows("Confirm"), view.TextOf("RemoveQuestion")));
        }, TestContext.Current.CancellationToken);
}

public sealed class ConnectionEditorViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheFormAsksForANameAHarnessAndWhereTheCredentialIsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConnectionEditorViewModel());

            Assert.Equal(("New connection", "claude-team", "TEAM_API_KEY"), (view.TextOf("Title"), view.Find<TextBox>("NameField").Text, view.Find<TextBox>("Reference").Text));
            Assert.Equal((1, 3), (view.Find<ComboBox>("Provider").ItemCount, view.Find<ComboBox>("Source").ItemCount));
            Assert.Contains("never reads or writes the key", view.TextOf("ReferenceHint"), StringComparison.Ordinal);
            Assert.False(view.Shows("Confirm"));
            Assert.Equal((true, 4, 6), (view.Shows("Model"), view.Find<ComboBox>("Model").ItemCount, view.Find<ComboBox>("Effort").ItemCount));
        }, TestContext.Current.CancellationToken);
}

public sealed class DefaultConnectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AutoIsShownRecommendedWithWhatItMeansAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDefaultConnectionViewModel());

            Assert.Equal((3, DefaultPhrases.Auto, true, false, false), (view.Find<ComboBox>("Choices").ItemCount, view.Find<ComboBox>("Choices").SelectedItem, view.Shows("Recommended"), view.Shows("Save"), view.Shows("Error")));
            Assert.StartsWith("Recommended.", view.TextOf("Explanation"), StringComparison.Ordinal);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task PickingAConnectionOffersToSaveAndSavingFixesItAsync() =>
        ui.RunAsync(async () =>
        {
            var connections = new FakeConnections("claude-work", "claude-personal").Automatic();
            var settings = new MachineSettings(connections, new FakeSupervision(), new FakeResources(), new FakeForgeCatalog());
            var chosen = new DefaultConnectionViewModel(settings, new CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger());
            chosen.Show(connections.Catalog);
            var view = Screen.Show(chosen);

            view.Find<ComboBox>("Choices").SelectedItem = "claude-personal";
            view.Settle();
            var offered = (view.Shows("Save"), view.Shows("Recommended"));
            view.Click("Save");
            await (chosen.SaveCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal((true, false), offered);
            Assert.Equal((false, "claude-personal"), (view.Shows("Save"), chosen.Saved));
            Assert.Contains("runs on claude-personal, even near its limit", view.TextOf("Explanation"), StringComparison.Ordinal);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARefusedChangeShowsWhyInPlaceAsync() =>
        ui.RunAsync(async () =>
        {
            var connections = new FakeConnections("claude-work", "claude-personal").Automatic();
            connections.Refusal = Agents.Contracts.Connections.ConnectionError.Unwritable;
            var chosen = new DefaultConnectionViewModel(new MachineSettings(connections, new FakeSupervision(), new FakeResources(), new FakeForgeCatalog()), new CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger());
            chosen.Show(connections.Catalog);
            var view = Screen.Show(chosen);

            view.Find<ComboBox>("Choices").SelectedItem = "claude-work";
            await chosen.SaveCommand.ExecuteAsync(null);
            view.Settle();

            Assert.Equal((true, "connections.json could not be written."), (view.Shows("Error"), view.TextOf("Error")));
        }, TestContext.Current.CancellationToken);
}

public sealed class RuleFileViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AFileEditedInTheCheckoutSaysSoAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignRuleFileViewModel(".avala/checks.json", "Applied", "4be19c2", true, "lint, vet"));

            Assert.Equal((".avala/checks.json", "4be19c2", true, "lint, vet"), (view.TextOf("Path"), view.TextOf("Commit"), view.Shows("Edited"), view.TextOf("Summary")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARejectedFileShowsItsStatusInTheFailureColorAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignRuleFileViewModel(".avala/checks.json", "Rejected: Malformed", "70d2e11", false, "no checks"));

            Assert.Equal(Color.Parse("#EF6461"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<TextBlock>("Status").Foreground).Color);
        }, TestContext.Current.CancellationToken);

    [Theory]
    [InlineData(".avala/budget.json", true)]
    [InlineData(".avala/jobs.json", false)]
    public Task OnlyAFileWithAKnownFormatOffersEditHereAsync(string path, bool offered) =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignRuleFileViewModel(path, "Applied", "4be19c2", false, "1 scope"));

            Assert.Equal((offered, true), (view.Shows("EditHere"), view.Shows("Edit")));
        }, TestContext.Current.CancellationToken);
}

public sealed class RuleFileEditorViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheEditorShowsTheFileItsNoteAndTheTextInTheCodeFontAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignRuleFileEditorViewModel());

            Assert.Equal(".avala/budget.json", view.TextOf("Path"));
            Assert.Contains("applies to new jobs once you commit it", view.TextOf("Note"), StringComparison.Ordinal);
            Assert.Contains("holdAtLimit", view.Find<TextBox>("FileText").Text, StringComparison.Ordinal);
            Assert.Contains("JetBrains Mono", view.Find<TextBox>("FileText").FontFamily.ToString(), StringComparison.Ordinal);
            Assert.False(view.Shows("Error"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARejectionShowsUnderTheTextInTheFailureColorAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignRuleFileEditorViewModel { Error = "Not saved: jobs would reject .avala/budget.json (InvalidThreshold). Fix it and save again." });

            Assert.True(view.Shows("Error"));
            Assert.Equal(Color.Parse("#EF6461"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<TextBlock>("Error").Foreground).Color);
        }, TestContext.Current.CancellationToken);
}

public sealed class RuleViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARuleShowsItsOrderOriginNameTargetAndAnswerAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignRuleViewModel());

            Assert.Equal(("3", "Repository", "tests", "go test *, go vet *, golangci-lint *", "Allow"), (view.TextOf("Order"), view.TextOf("Origin"), view.TextOf("RuleName"), view.TextOf("Target"), view.TextOf("Answer")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ADenyIsRedAndAnAskIsAmberAsync() =>
        ui.RunAsync(() =>
        {
            var deny = Screen.Show(new DesignRuleViewModel(1, "outside", "Built-in", "any", "anything", PolicyAnswer.Deny));
            var denied = Assert.IsAssignableFrom<ISolidColorBrush>(deny.Find<TextBlock>("Answer").Foreground).Color;
            var ask = Screen.Show(new DesignRuleViewModel(2, "migrations", "Repository", "FileEdit", "migrations/**", PolicyAnswer.Ask));

            Assert.Equal((Color.Parse("#EF6461"), Color.Parse("#E5A13A")), (denied, Assert.IsAssignableFrom<ISolidColorBrush>(ask.Find<TextBlock>("Answer").Foreground).Color));
        }, TestContext.Current.CancellationToken);
}

public sealed class CapsViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task CapsShowTheirScopeAndOneLinePerLimitAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignCapsViewModel());

            Assert.Equal(("Every connection", 3), (view.TextOf("Scope"), view.Find<ItemsControl>("Lines").ItemCount));
            Assert.Contains("Hold when a usage window reaches", view.VisibleTexts);
        }, TestContext.Current.CancellationToken);
}

public sealed class CheckViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ACheckShowsItsNameCommandAndTimeoutAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignCheckViewModel());

            Assert.Equal(("test", "go test ./...", "300s"), (view.TextOf("CheckName"), view.TextOf("Command"), view.TextOf("Timeout")));
        }, TestContext.Current.CancellationToken);
}

public sealed class JobSectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ASectionShowsItsNameAndValueAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobSectionViewModel());

            Assert.Equal(("approval", "merge"), (view.TextOf("SectionName"), view.Find<SelectableTextBlock>("Value").Text));
        }, TestContext.Current.CancellationToken);
}

public sealed class MachineConnectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AConnectionShowsItsNameWhetherItIsTheDefaultItsSourceAndItsOriginAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignMachineConnectionViewModel("claude-personal", "the provider's own login", false));

            Assert.Equal(
                ("claude-personal", false, "the provider's own login", "discovered on this machine"),
                (view.TextOf("ConnectionName"), view.Shows("Default"), view.TextOf("Source"), view.TextOf("Origin")));
        }, TestContext.Current.CancellationToken);
}
