using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Contracts;
using Avala.Workspaces.Git;
using Avala.Workspaces.Scratch;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Workspaces.Tests.Scratch;

public sealed class ScratchRepositoriesTests
{
    private static readonly IProcessRunner Processes =
        new ServiceCollection().AddRuntime(new AvalaPaths(Path.GetTempPath())).BuildServiceProvider().GetRequiredService<IProcessRunner>();

    private static readonly string[] Identity = ["user.name=Ada", "user.email=ada@localhost", "commit.gpgsign=false", "init.defaultBranch=trunk"];

    private static readonly string[] Nobody = ["user.name=", "user.email=", "init.defaultBranch=trunk"];

    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2026, 10, 12, 9, 30, 0, TimeSpan.Zero));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AScratchRepositoryIsNamedAfterTheDayAndTheFirstWordsOfItsInstructionAndMergesWhatIsApprovedAsync()
    {
        await using var data = new TemporaryFolder();
        var scratch = Scratch(data, Identity, "a1b2c3");

        var made = Outcomes.Succeeds(await scratch.CreateAsync("Fix JPY rounding in invoice totals, please", Cancellation));

        Assert.Equal(Path.Combine(Path.GetFullPath(data.Path), "scratch", "2026-10-12-fix-jpy-rounding-in-a1b2c3"), made);
        Assert.Equal(
            ("1", "trunk", ".avala/jobs.json", "Ada", string.Empty),
            (await GitAsync(made, "rev-list", "--count", "HEAD"), await GitAsync(made, "branch", "--show-current"), await GitAsync(made, "ls-tree", "-r", "--name-only", "HEAD"), await GitAsync(made, "log", "-1", "--format=%an"), await GitAsync(made, "status", "--porcelain")));
        Assert.Contains("\"merge\"", await File.ReadAllTextAsync(Path.Combine(made, ".avala", "jobs.json"), Cancellation), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Add an endpoint", "add-an-endpoint")]
    [InlineData("  Café: ÉTÉ / summer_notes!! ", "caf-t-summer-notes")]
    [InlineData("¿?", "job")]
    [InlineData("Supercalifragilisticexpialidocious antidisestablishmentarianism", "supercalifragilisticexpialidocious-antid")]
    public void TheNameKeepsAtMostFourPlainWordsOfTheInstruction(string instruction, string slug) =>
        Assert.Equal($"2026-10-12-{slug}-0f0f0f", ScratchName.Of(new DateOnly(2026, 10, 12), instruction, "0f0f0f"));

    [Fact]
    public async Task ANameAlreadyTakenIsPassedOverForAFreshIdAsync()
    {
        await using var data = new TemporaryFolder();
        var taken = Directory.CreateDirectory(Path.Combine(data.Path, "scratch", "2026-10-12-add-an-endpoint-aaaaaa")).FullName;
        await File.WriteAllTextAsync(Path.Combine(taken, "keep.txt"), "mine", Cancellation);

        var made = Outcomes.Succeeds(await Scratch(data, Identity, "aaaaaa", "bbbbbb").CreateAsync("Add an endpoint", Cancellation));

        Assert.Equal(("2026-10-12-add-an-endpoint-bbbbbb", "keep.txt"), (Path.GetFileName(made), string.Join(",", Directory.GetFiles(taken).Select(Path.GetFileName))));
    }

    [Fact]
    public async Task WithoutAGitIdentityTheFirstCommitIsMadeUnderAvalasNameAsync()
    {
        await using var data = new TemporaryFolder();

        var made = Outcomes.Succeeds(await Scratch(data, Nobody, "c0ffee").CreateAsync("Sketch a parser", Cancellation));

        Assert.Equal("Avala <avala@localhost>", await GitAsync(made, "log", "-1", "--format=%an <%ae>"));
    }

    [Fact]
    public async Task OnlyAFolderInsideTheScratchFolderIsAScratchRepositoryAsync()
    {
        await using var data = new TemporaryFolder();
        var scratch = Scratch(data, Identity, "a1b2c3");
        var folder = Path.Combine(data.Path, "scratch");

        Assert.Equal(
            [true, false, false, false, false],
            [scratch.Holds(Path.Combine(folder, "2026-10-12-job-a1b2c3")), scratch.Holds(folder), scratch.Holds(folder + "-other"), scratch.Holds(data.Path), scratch.Holds(" ")]);
    }

    private static ScratchRepositories Scratch(TemporaryFolder data, string[] settings, params string[] ids)
    {
        var next = new Queue<string>(ids);

        return new(new GitRepositories(new ConfiguredGit(Processes, settings)), new ScratchSettings(Path.Combine(data.Path, "scratch")) { Ids = next.Dequeue }, Clock);
    }

    private static async Task<string> GitAsync(string folder, params string[] arguments) =>
        (await Processes.RunAsync(new ProcessRequest("git", ["-C", folder, .. arguments]), Cancellation)).Match(outcome => outcome.Output.Trim(), error => error.ToString());

    private sealed class ConfiguredGit(IProcessRunner inner, IReadOnlyList<string> settings) : IProcessRunner
    {
        public ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken) =>
            inner.RunAsync(request with { Arguments = [.. settings.SelectMany(setting => new[] { "-c", setting }), .. request.Arguments] }, cancellationToken);
    }
}
