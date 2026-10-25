using Avala.Resources.Contracts;
using Avala.Resources.Settings;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Resources.Tests.Settings;

public sealed class ResourceSettingsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void AFileWithEverySectionIsApplied()
    {
        var settings = Outcomes.Succeeds(ResourceSettingsParser.Parse("""
            {
              "sampleSeconds": 2.5,
              "diskSeconds": 30,
              "orphans": "report",
              "ports": { "first": 30000, "last": 30099, "perWorktree": 20 },
              "worktrees": {
                "freeArchivedHours": 0,
                "freeDeliveredHours": 2,
                "freePullRequestHours": null,
                "freeUndeliveredHours": 48,
                "freeDiscardedHours": 1,
                "freeFailedHours": 0.5,
                "deleteDiscardedHours": 24,
                "deleteFailedHours": null,
                "freeIdleHours": 12,
                "spaceBudgetGigabytes": 2.5,
                "diskFloorPercent": null,
                "reconcile": "report"
              }
            }
            """));

        Assert.Equal(
            new ResourceSettings(
                TimeSpan.FromSeconds(2.5),
                TimeSpan.FromSeconds(30),
                OrphanPolicy.Report,
                new PortRange(30_000, 30_099, 20),
                new WorktreePolicy(TimeSpan.Zero, TimeSpan.FromHours(2), Option<TimeSpan>.None, TimeSpan.FromHours(48), TimeSpan.FromHours(1), TimeSpan.FromMinutes(30))
                {
                    DeleteDiscarded = TimeSpan.FromHours(24),
                    DeleteFailed = Option<TimeSpan>.None,
                    Idle = TimeSpan.FromHours(12),
                    SpaceBudget = 2_684_354_560,
                    DiskFloor = Option<double>.None,
                },
                ReconcilePolicy.Report),
            settings);
    }

    [Fact]
    public void TheEarlierRetentionFieldsStillSetWhenWorkIsDeletedAndWhenUndeliveredWorkIsFreed()
    {
        var policy = Outcomes.Succeeds(ResourceSettingsParser.Parse("""
            { "worktrees": { "keepDiscardedHours": 1, "keepFailedHours": null, "keepApprovedHours": 24 } }
            """)).Worktrees;

        Assert.Equal(
            ResourceSettingsParser.Defaults.Worktrees with { Undelivered = TimeSpan.FromHours(24), DeleteDiscarded = TimeSpan.FromHours(1), DeleteFailed = Option<TimeSpan>.None },
            policy);
    }

    [Fact]
    public void ANewFieldWinsOverTheEarlierFieldItReplaces() =>
        Assert.Equal(
            TimeSpan.FromHours(2),
            Outcomes.Present(Outcomes.Succeeds(ResourceSettingsParser.Parse("""{ "worktrees": { "keepDiscardedHours": 1, "deleteDiscardedHours": 2 } }""")).Worktrees.DeleteDiscarded));

    [Fact]
    public async Task ChangingTheWorktreePolicyWritesItAndKeepsTheRestOfTheFileAsync()
    {
        await using var data = new TemporaryFolder();
        var path = Path.Combine(data.Path, ResourceSettingsFile.FileName);
        await File.WriteAllTextAsync(path, """{ "sampleSeconds": 2, "worktrees": { "keepFailedHours": 5, "reconcile": "clean" } }""", Cancellation);
        await using var file = new ResourceSettingsFile(new AvalaPaths(data.Path));
        var policy = ResourceSettingsParser.Defaults.Worktrees with { Archived = Option<TimeSpan>.None, DeleteFailed = TimeSpan.FromHours(3) };

        var changed = Outcomes.Succeeds(await file.ChangeWorktreesAsync(policy, Cancellation));
        await using var reread = new ResourceSettingsFile(new AvalaPaths(data.Path));
        var read = await reread.LoadAsync(Cancellation);

        Assert.Equal((policy, TimeSpan.FromSeconds(2), ReconcilePolicy.Clean, ResourceFileStatus.Applied), (changed.Worktrees, changed.Sampling, changed.Reconcile, changed.File));
        Assert.Equal(changed, read);
        Assert.Equal(changed, await file.LoadAsync(Cancellation));
        Assert.DoesNotContain("keepFailedHours", await File.ReadAllTextAsync(path, Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChangingTheWorktreePolicyWithoutAFileCreatesItAsync()
    {
        await using var data = new TemporaryFolder();
        await using var file = new ResourceSettingsFile(new AvalaPaths(data.Path));

        var changed = Outcomes.Succeeds(await file.ChangeWorktreesAsync(ResourceSettingsParser.Defaults.Worktrees with { Failed = TimeSpan.FromDays(2) }, Cancellation));

        Assert.Equal(ResourceSettingsParser.Defaults with { Worktrees = ResourceSettingsParser.Defaults.Worktrees with { Failed = TimeSpan.FromDays(2) }, File = ResourceFileStatus.Applied }, changed);
        Assert.True(File.Exists(Path.Combine(data.Path, ResourceSettingsFile.FileName)));
    }

    [Fact]
    public async Task ARejectedFileIsNotOverwrittenByAPolicyChangeAsync()
    {
        await using var data = new TemporaryFolder();
        var path = Path.Combine(data.Path, ResourceSettingsFile.FileName);
        await File.WriteAllTextAsync(path, """{ "orphans": "ignore" }""", Cancellation);
        await using var file = new ResourceSettingsFile(new AvalaPaths(data.Path));

        Assert.Equal(ResourceError.UnknownPolicy, Outcomes.FailsWith(await file.ChangeWorktreesAsync(ResourceSettingsParser.Defaults.Worktrees, Cancellation)));
        Assert.Equal("""{ "orphans": "ignore" }""", await File.ReadAllTextAsync(path, Cancellation));
    }

    [Fact]
    public void AnEmptyFileKeepsEveryDefault() =>
        Assert.Equal(ResourceSettingsParser.Defaults, Outcomes.Succeeds(ResourceSettingsParser.Parse("{}")));

    [Theory]
    [InlineData("[]", ResourceError.Malformed)]
    [InlineData("{ \"sampleSeconds\": 1, \"sampleSeconds\": 2 }", ResourceError.Malformed)]
    [InlineData("{ \"sampleSeconds\": \"5\" }", ResourceError.Malformed)]
    [InlineData("{ \"cpu\": 1 }", ResourceError.UnknownField)]
    [InlineData("{ \"ports\": { \"from\": 1 } }", ResourceError.UnknownField)]
    [InlineData("{ \"sampleSeconds\": 0 }", ResourceError.InvalidInterval)]
    [InlineData("{ \"diskSeconds\": 90000 }", ResourceError.InvalidInterval)]
    [InlineData("{ \"orphans\": \"ignore\" }", ResourceError.UnknownPolicy)]
    [InlineData("{ \"orphans\": \"1\" }", ResourceError.UnknownPolicy)]
    [InlineData("{ \"worktrees\": { \"reconcile\": \"delete\" } }", ResourceError.UnknownPolicy)]
    [InlineData("{ \"ports\": { \"first\": 80, \"last\": 100 } }", ResourceError.InvalidPorts)]
    [InlineData("{ \"ports\": { \"first\": 30010, \"last\": 30000 } }", ResourceError.InvalidPorts)]
    [InlineData("{ \"ports\": { \"first\": 30000, \"last\": 30009, \"perWorktree\": 11 } }", ResourceError.InvalidPorts)]
    [InlineData("{ \"ports\": { \"first\": 1.5 } }", ResourceError.Malformed)]
    [InlineData("{ \"worktrees\": { \"keepFailedHours\": -1 } }", ResourceError.InvalidRetention)]
    [InlineData("{ \"worktrees\": { \"keepFailedHours\": \"ever\" } }", ResourceError.Malformed)]
    [InlineData("{ \"worktrees\": { \"freeArchivedHours\": -1 } }", ResourceError.InvalidRetention)]
    [InlineData("{ \"worktrees\": { \"freeEverythingHours\": 1 } }", ResourceError.UnknownField)]
    [InlineData("{ \"worktrees\": { \"spaceBudgetGigabytes\": 0 } }", ResourceError.InvalidRetention)]
    [InlineData("{ \"worktrees\": { \"diskFloorPercent\": 95 } }", ResourceError.InvalidRetention)]
    public void AnInvalidFileIsRejectedWithItsError(string text, ResourceError expected) =>
        Assert.Equal(expected, Outcomes.FailsWith(ResourceSettingsParser.Parse(text)));

    [Fact]
    public async Task WithoutAFileTheDefaultsApplyAsync()
    {
        using var data = new TemporaryFolder();

        await using var file = new ResourceSettingsFile(new AvalaPaths(data.Path));
        var settings = await file.LoadAsync(Cancellation);

        Assert.Equal(ResourceSettingsParser.Defaults with { File = ResourceFileStatus.Absent }, settings);
        Assert.Equal(
            (TimeSpan.FromSeconds(5), OrphanPolicy.Kill, new PortRange(24_000, 24_999, 10), ReconcilePolicy.Clean),
            (settings.Sampling, settings.Orphans, settings.Ports, settings.Reconcile));
        Assert.Equal(
            new WorktreePolicy(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromDays(1), TimeSpan.Zero, TimeSpan.Zero)
            {
                DeleteDiscarded = TimeSpan.FromDays(7),
                DeleteFailed = TimeSpan.FromDays(30),
                Idle = TimeSpan.FromDays(3),
                SpaceBudget = 20L << 30,
                DiskFloor = 0.1,
            },
            settings.Worktrees);
    }

    [Theory]
    [InlineData("{ \"orphans\": \"ignore\" }", ResourceError.UnknownPolicy)]
    [InlineData(null, ResourceError.TooLarge)]
    public async Task ARejectedFileKeepsTheDefaultsAndSaysWhyAsync(string? text, ResourceError expected)
    {
        using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(
            Path.Combine(data.Path, ResourceSettingsFile.FileName),
            text ?? $"{{ \"sampleSeconds\": 5 {new string(' ', ResourceSettingsFile.MaximumBytes)}}}",
            Cancellation);

        await using var file = new ResourceSettingsFile(new AvalaPaths(data.Path));
        var settings = await file.LoadAsync(Cancellation);

        Assert.Equal(ResourceSettingsParser.Defaults with { File = ResourceFileStatus.Rejected, Error = expected }, settings);
    }
}
