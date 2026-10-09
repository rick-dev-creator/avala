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
              "worktrees": { "keepDiscardedHours": 1, "keepFailedHours": null, "keepApprovedHours": 24, "reconcile": "clean" }
            }
            """));

        Assert.Equal(
            new ResourceSettings(
                TimeSpan.FromSeconds(2.5),
                TimeSpan.FromSeconds(30),
                OrphanPolicy.Report,
                new PortRange(30_000, 30_099, 20),
                new WorktreeRetention(TimeSpan.FromHours(1), Option<TimeSpan>.None, TimeSpan.FromHours(24)),
                ReconcilePolicy.Clean),
            settings);
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
    public void AnInvalidFileIsRejectedWithItsError(string text, ResourceError expected) =>
        Assert.Equal(expected, Outcomes.FailsWith(ResourceSettingsParser.Parse(text)));

    [Fact]
    public async Task WithoutAFileTheDefaultsApplyAsync()
    {
        using var data = new TemporaryFolder();

        var settings = await new ResourceSettingsFile(new AvalaPaths(data.Path)).LoadAsync(Cancellation);

        Assert.Equal(ResourceSettingsParser.Defaults with { File = ResourceFileStatus.Absent }, settings);
        Assert.Equal(
            (TimeSpan.FromSeconds(5), OrphanPolicy.Kill, new PortRange(24_000, 24_999, 10), TimeSpan.Zero, TimeSpan.FromDays(7), Option<TimeSpan>.None, ReconcilePolicy.Report),
            (settings.Sampling, settings.Orphans, settings.Ports, Outcomes.Present(settings.Retention.Discarded), Outcomes.Present(settings.Retention.Failed), settings.Retention.Approved, settings.Reconcile));
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

        var settings = await new ResourceSettingsFile(new AvalaPaths(data.Path)).LoadAsync(Cancellation);

        Assert.Equal(ResourceSettingsParser.Defaults with { File = ResourceFileStatus.Rejected, Error = expected }, settings);
    }
}
