using Avala.Components.Privacy;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Tests.Accounts;
using Avala.Workbench.Tests.RepositoryChoice;
using Avala.Workbench.Welcome;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Welcome;

public sealed class WelcomeViewModelScripts
{
    private readonly SubmittingJobs jobs = new();
    private readonly FakeConnections connections = new("claude-work");
    private readonly FakeSourceControl forges = new();
    private readonly FakeScratch scratch = new();
    private readonly FakeLinks links = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void TheWholeWelcomeShowsOnAnEmptyBoardAndOnlyTheComposerOnceJobsExist()
    {
        var welcome = Welcome();
        var empty = welcome.IsCompact;

        welcome.ShowBoard(hasJobs: true);

        Assert.Equal((false, true), (empty, welcome.IsCompact));
    }

    [Fact]
    public async Task EnterStartsTheInstructionInAScratchRepositoryOnAutoAndSelectsTheJobAsync()
    {
        var welcome = await CheckedAsync();
        welcome.Instruction = " Sketch a CSV parser ";

        await welcome.StartCommand.ExecuteAsync(null);

        var request = Assert.Single(jobs.Requests);
        Assert.Equal(("/data/scratch/2026-10-12-scratch-1", "Sketch a CSV parser", Option<ConnectionName>.None), (request.RepositoryPath, request.Instruction, request.Connection));
        Assert.Equal(["Sketch a CSV parser"], scratch.Instructions);
        Assert.Single(links.Selected);
        Assert.Equal(string.Empty, welcome.Instruction);
    }

    [Fact]
    public async Task EnterStartsOnTheConnectionModelAndEffortChosenOnTheChipAndTheNextChatKeepsThemAsync()
    {
        connections.Catalog = connections.Catalog with { Connections = [.. connections.Catalog.Connections, new DeclaredConnection(new ConnectionName("claude-personal"), "simulator", "login")] };
        connections.Offers["claude-personal"] = CapabilitySet.Of(new OffersModels(["large", "small"], ["low", "high"]));
        var welcome = await CheckedAsync();
        var harness = (HarnessChoiceViewModel)welcome.Harness;
        harness.Options[2].ChooseCommand.Execute(null);
        await harness.Offering;
        harness.Models.Model = "large";
        harness.Models.Effort = "high";
        welcome.Instruction = "Sketch a CSV parser";

        await welcome.StartCommand.ExecuteAsync(null);
        await welcome.CheckAsync(Cancellation);

        var request = Assert.Single(jobs.Requests);
        Assert.Equal((new ConnectionName("claude-personal"), new ModelChoice("large", "high")), (request.Connection.Match(name => name, () => new ConnectionName("none")), request.Model));
        Assert.Equal("claude-personal · large · high", welcome.Harness.Label);
    }

    [Fact]
    public async Task AScratchRepositoryThatCannotBeMadeSaysWhyAndStartsNothingAsync()
    {
        scratch.Refusal = WorkspaceFailure.FileUnwritable;
        var welcome = await CheckedAsync();
        welcome.Instruction = "Sketch a CSV parser";

        await welcome.StartCommand.ExecuteAsync(null);

        Assert.Equal((0, "Sketch a CSV parser"), (jobs.Requests.Count, welcome.Instruction));
        Assert.NotEmpty(welcome.Error);
    }

    [Fact]
    public async Task WithoutAnyHarnessTheComposerIsDisabledAndOneAmberChipChecksAgainForALoginAsync()
    {
        var ready = connections.Catalog;
        connections.Catalog = connections.Catalog with { Connections = [] };
        connections.Discovered = ready;
        var welcome = await CheckedAsync();
        welcome.Instruction = "Sketch a CSV parser";
        var chip = Assert.Single(welcome.Chips);
        var blocked = (welcome.CanCompose, welcome.StartCommand.CanExecute(null), chip.Label.ToString(), chip.IsAttention, chip.FixLabel);

        await chip.FixCommand.ExecuteAsync(null);

        Assert.Equal((false, false, WelcomePhrases.NoAgent, true, WelcomePhrases.CheckAgain), blocked);
        Assert.Equal((true, true, "claude-work"), (welcome.CanCompose, welcome.StartCommand.CanExecute(null), Assert.Single(welcome.Chips).Label.ToString()));
    }

    [Fact]
    public async Task ARejectedConnectionsFileOffersToOpenSettingsAsync()
    {
        connections.Catalog = connections.Catalog with { File = ConnectionFileStatus.Rejected, Connections = [] };
        var welcome = await CheckedAsync();

        await Assert.Single(welcome.Chips).FixCommand.ExecuteAsync(null);

        Assert.Equal((false, 1), (welcome.CanCompose, links.SettingsOpened));
    }

    [Fact]
    public async Task EveryHarnessConnectionAndForgeAccountHasAChipThatTurnsAmberWhenItCannotWorkAsync()
    {
        forges.Accounts.AddRange([
            Kinds.Declared("hub", Kinds.Hub, CredentialSource.Cli, login: "octocat"),
            Kinds.Declared("work", Kinds.Hub, CredentialSource.Environment, problem: ForgeError.MissingCredential),
            Kinds.Detected("cup", Kinds.Cup, CredentialSource.Cli, "ada", "cup.example"),
        ]);

        var welcome = await CheckedAsync();

        Assert.Equal(
            [("claude-work", "CW", false, false, string.Empty), ("oc•••••", "O", true, false, string.Empty), ("work", "W", true, true, WelcomePhrases.CheckAgain)],
            welcome.Chips.Select(chip => (chip.Label.ToString(), chip.Monogram, chip.HasLogo, chip.IsAttention, chip.FixLabel)));
    }

    [Fact]
    public async Task TheCardsOpenTheirSheetInNewJobAndConnectOpensTheConnectSheetAsync()
    {
        var welcome = await CheckedAsync();

        await welcome.OpenFolderCommand.ExecuteAsync(null);
        await welcome.CloneCommand.ExecuteAsync(null);
        await welcome.NewRepositoryCommand.ExecuteAsync(null);
        await welcome.ConnectCommand.ExecuteAsync(null);

        Assert.Equal([RepositoryStart.OpenFolder, RepositoryStart.Clone, RepositoryStart.NewRepository], links.Starts);
        Assert.Equal(1, links.Connects);
    }

    private WelcomeViewModel Welcome()
    {
        var launch = Launching.Launch(jobs, connections, new FakePreview(), new FakePolicies(), scratch: scratch);

        return new(launch, forges.Source, links, HarnessChoiceViewModelScripts.Harness(launch));
    }

    private async Task<WelcomeViewModel> CheckedAsync()
    {
        var welcome = Welcome();
        await welcome.CheckAsync(Cancellation);

        return welcome;
    }

    private sealed class FakeLinks : IWelcomeLinks
    {
        public List<JobId> Selected { get; } = [];

        public List<RepositoryStart> Starts { get; } = [];

        public int Connects { get; private set; }

        public int SettingsOpened { get; private set; }

        public void Select(JobId job) => Selected.Add(job);

        public Task StartAsync(RepositoryStart start)
        {
            Starts.Add(start);

            return Task.CompletedTask;
        }

        public Task ConnectAsync()
        {
            Connects++;

            return Task.CompletedTask;
        }

        public void OpenSettings() => SettingsOpened++;
    }
}

public sealed class AccountChipViewModelScripts
{
    [Fact]
    public void AnAgentChipShowsItsMonogramAndAForgeChipItsLogo()
    {
        var agent = AccountChipViewModel.Agent("claude-personal", attention: false, string.Empty, new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask));
        var forge = AccountChipViewModel.Forge(Personal.Login("octocat"), Kinds.Hub.Logo, attention: false, string.Empty, new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask));

        Assert.Equal(("CP", false, "oc•••••", true), (agent.Monogram, agent.HasLogo, forge.Label.ToString(), forge.HasLogo));
    }
}
