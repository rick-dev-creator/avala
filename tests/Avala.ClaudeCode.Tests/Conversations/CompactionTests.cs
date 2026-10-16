using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Cli;
using Avala.ClaudeCode.Conversations;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class CompactionTests
{
    private const string Gate = "AVALA_REAL_CLAUDE";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null, "250000")]
    [InlineData("120000", "120000")]
    [InlineData("lots", "250000")]
    [InlineData("-5", "250000")]
    public void ASessionCompactsAtTwoHundredFiftyThousandTokensUnlessItsConnectionSetsCompactAt(string? compactAt, string window) =>
        Assert.Equal(window, Launch(compactAt is null ? [] : [(CommandLine.CompactAtSetting, compactAt)]).Variables[CommandLine.CompactWindowVariable]);

    [Fact]
    public void ASessionWhoseRoleSetsACompactionWindowCompactsThereInsteadOfAtItsConnections() =>
        Assert.Equal("90000", Launch([(CommandLine.CompactAtSetting, "120000")], window: 90_000L).Variables[CommandLine.CompactWindowVariable]);

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void AConnectionTurnsAutomaticAndIdleCompactionOnOrOffWithOrWithoutTheUsersConfiguration(string userConfiguration)
    {
        var launch = Launch([(CommandLine.AutoCompactSetting, "false"), (CommandLine.IdleCompactionSetting, "false"), (CommandLine.UserConfigurationSetting, userConfiguration)]);
        var settings = JsonNode.Parse(launch.Arguments[launch.Arguments.ToList().IndexOf("--settings") + 1])!;

        Assert.Equal((false, false), (settings["autoCompactEnabled"]!.GetValue<bool>(), settings["idleCompaction"]!.GetValue<bool>()));
    }

    [Fact]
    public void WithoutCompactionSettingsTheHarnessKeepsItsOwnAutomaticAndIdleCompaction()
    {
        var launch = Launch([(CommandLine.UserConfigurationSetting, "false")]);

        Assert.DoesNotContain("--settings", launch.Arguments);
    }

    [Fact]
    public void ACompactionWindowAvalaInheritsNeverReachesTheHarness() =>
        Assert.Contains(CommandLine.CompactWindowVariable, Launch([]).Cleared);

    [Fact]
    public async Task TheRealHarnessCompactsAtTheConnectionsWindowAndHonoursItsAutomaticCompactionAsync()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Gate) == "1", $"Set {Gate}=1 to start the real Claude Code CLI.");
        using var temporary = new TemporaryFolder();

        var standard = await ContextAsync(temporary.Path, []);
        var smaller = await ContextAsync(temporary.Path, [(CommandLine.CompactAtSetting, "120000")]);
        var off = await ContextAsync(temporary.Path, [(CommandLine.AutoCompactSetting, "false")]);

        Assert.Equal((250_000, "env", true), Reading(standard));
        Assert.Equal((120_000, "env", true), Reading(smaller));
        Assert.False(off["isAutoCompactEnabled"]!.GetValue<bool>());
        Assert.True(standard["autoCompactThreshold"]!.GetValue<int>() < 250_000);
    }

    private static (int, string, bool) Reading(JsonNode context) =>
        (context["maxTokens"]!.GetValue<int>(), context["autocompactSource"]!.GetValue<string>(), context["isAutoCompactEnabled"]!.GetValue<bool>());

    private static CliLaunch Launch((string Name, string Value)[] settings, string folder = "/repo", Option<long> window = default) =>
        CommandLine.For(
            folder,
            new SessionOptions(folder, PermissionMode.AskEveryTime)
            {
                Connection = new ConnectionEnvironment
                {
                    Settings = settings.ToDictionary(setting => setting.Name, setting => setting.Value),
                },
                Model = ModelChoice.Default with { CompactAt = window },
            },
            Option<ConversationMark>.None,
            new UserHome(folder, []));

    private static async Task<JsonNode> ContextAsync(string folder, (string Name, string Value)[] settings)
    {
        var launch = Launch([(CommandLine.UserConfigurationSetting, "false"), .. settings], folder);
        var cli = new ProcessCli(CliCommand.Installed, TimeProvider.System, NullLogger<ProcessCli>.Instance).Start(launch, UncontainedProcesses.Instance, Option<string>.None)
            .Match(started => started, error => throw new InvalidOperationException(error.ToString()));
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        guard.CancelAfter(TimeSpan.FromMinutes(1));

        await using (cli)
        {
            await cli.WriteAsync(Messages.Initialize().ToJsonString(), Cancellation);
            await cli.WriteAsync("""{"type":"control_request","request_id":"context","request":{"subtype":"get_context_usage"}}""", Cancellation);

            await foreach (var line in cli.Lines.WithCancellation(guard.Token))
            {
                if (JsonNode.Parse(line)?["response"] is { } response && response["request_id"]?.GetValue<string>() == "context")
                {
                    return response["response"]!;
                }
            }
        }

        throw new InvalidOperationException("The harness never reported its context.");
    }
}
