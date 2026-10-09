using System.Text.Json;
using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Host.Composition;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Host.Tests;

public sealed class RealClaudeCodeTests(PublishedPlugins plugins)
{
    private const string Gate = "AVALA_REAL_CLAUDE";

    private const string Model = "haiku";

    private const string Autonomous = """{ "autonomy": "autonomous" }""";

    private static readonly HarnessTool FollowUp = new(
        "propose_follow_up",
        "Propose a task to do after this one.",
        """{ "type": "object", "properties": { "instruction": { "type": "string" } }, "required": ["instruction"] }""",
        ToolSurface.Executed);

    private static readonly string[] AccountFields = ["emailAddress", "accountUuid", "organizationUuid", "displayName", "fullName", "organizationName"];

    private static readonly string Home =Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Output => Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE_OUT") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Path.GetTempPath(), "avala-real-claude");

    private static string Login => Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE_LOGIN") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Home, ".claude-work");

    private static string SecondLogin => Environment.GetEnvironmentVariable("AVALA_REAL_CLAUDE_SECOND_LOGIN") is { Length: > 0 } folder
        ? folder
        : Path.Combine(Home, ".claude");

    [Fact]
    public Task AnEditJobWithACanvasRunsEndToEndAsync() =>
        JobAsync(
            "claude-code-edit",
            "Create a file GREETING.md containing exactly one line: # Hello. Then show it with `cat GREETING.md`. "
            + "Then draw a tiny SVG circle with the canvas tool. Be brief.",
            Autonomous,
            ["GREETING.md"]);

    [Fact]
    public Task AQuestionJobIsAnsweredByThePolicyAsync() =>
        JobAsync(
            "claude-code-question",
            "Use the AskUserQuestion tool to ask me which greeting to use, with the options 'Hello (Recommended)' and 'Hi'. "
            + "Then use the Write tool to write only the chosen word into GREETING.md. Be brief.",
            Autonomous,
            ["GREETING.md"]);

    [Fact]
    public Task ADeniedCommandIsNeverRunAsync() =>
        JobAsync(
            "claude-code-denial",
            "Run the command `ls` with the Bash tool and tell me what you see. If it is denied, stop and say so. Be brief.",
            """{ "rules": [ { "name": "no commands", "kind": "command", "answer": "deny" } ] }""",
            []);

    [Fact]
    public Task APlanApprovalJobRunsEndToEndAsync() =>
        JobAsync(
            "claude-code-real-plan-approval",
            "Call the EnterPlanMode tool. Then call ExitPlanMode with the one-line plan 'Write GREETING.md containing # Hello'. "
            + "Once it is approved, write that file with the Write tool. Be brief.",
            Autonomous,
            ["GREETING.md"]);

    [Fact]
    public Task ASubagentJobRunsEndToEndAsync() =>
        JobAsync(
            "claude-code-real-subagent",
            "Use the Task tool with subagent_type general-purpose to read README.md and report its first line. Then say done in one word.",
            Autonomous,
            [],
            ("README.md", "# A service that greets the team\n"));

    [Fact]
    public async Task SessionsResumeCallHarnessToolsAndKeepTwoLoginsApartAsync()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Gate) == "1", $"Set {Gate}=1 to run real Claude Code sessions.");
        using var data = new TemporaryFolder();
        using var repository = new TemporaryFolder();
        await using var root = CompositionRoot.Create(plugins.Directory, new AvalaPaths(data.Path));
        var provider = root.Services.GetServices<IAgentProvider>().Single(candidate => candidate.Info.Id == "claude-code");
        var transcripts = Path.Combine(data.Path, "transcripts");
        var work = Connection(Login, Path.Combine(transcripts, "follow-up"));

        var called = await TalkAsync(provider, new SessionOptions(repository.Path, PermissionMode.AskEveryTime) { Connection = work, Tools = [FollowUp] },
            "Call the propose_follow_up tool with the instruction 'Announce the greeting'. Then remember the word avocado. Be brief.");
        var token = called.OfType<ResumeTokenIssued>().Last().Token;
        var resumed = await TalkAsync(provider, new SessionOptions(repository.Path, PermissionMode.AskEveryTime) { Connection = Connection(Login, Path.Combine(transcripts, "resume")), Resume = token },
            "Which word did I ask you to remember? One word.");
        var other = await TalkAsync(provider, new SessionOptions(repository.Path, PermissionMode.AskEveryTime) { Connection = Connection(SecondLogin, Path.Combine(transcripts, "second-login")) },
            "Say hello in two words.");
        var foreign = await provider.StartAsync(new SessionOptions(repository.Path, PermissionMode.AskEveryTime) { Connection = Connection(SecondLogin, transcripts), Resume = token }, Cancellation);

        await SaveTranscriptsAsync(transcripts, "sessions", repository.Path);
        await SaveCostAsync("sessions", [.. called, .. resumed, .. other]);

        Assert.All(new[] { called, resumed, other }, events => Assert.Equal(TurnOutcome.Finished, events.OfType<TurnCompleted>().Single().Outcome));
        Assert.Contains(called, agentEvent => agentEvent is ToolReturned);
        Assert.Contains("avocado", string.Concat(resumed.OfType<ItemProgressed>().Select(progressed => progressed.Text)), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AgentError.CannotResume, foreign.Match(_ => default, error => error));
    }

    [Fact]
    public async Task AMessageSentMidTurnJoinsTheTurnAndAnInterruptionLeavesTheNextTurnWholeAsync()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Gate) == "1", $"Set {Gate}=1 to run real Claude Code sessions.");
        using var data = new TemporaryFolder();
        using var repository = new TemporaryFolder();
        await using var root = CompositionRoot.Create(plugins.Directory, new AvalaPaths(data.Path));
        var provider = root.Services.GetServices<IAgentProvider>().Single(candidate => candidate.Info.Id == "claude-code");
        var transcripts = Path.Combine(data.Path, "transcripts");
        var session = (await provider.StartAsync(new SessionOptions(repository.Path, PermissionMode.AskEveryTime) { Connection = Connection(Login, transcripts) }, Cancellation))
            .Match(started => started, error => throw new InvalidOperationException(error.ToString()));
        var steered = new List<IAgentEvent>();
        var interrupted = new List<IAgentEvent>();
        var after = new List<IAgentEvent>();

        await using (session)
        {
            await using var events = session.Events.GetAsyncEnumerator(Cancellation);
            await TurnAsync(session, events, steered, "Count from 1 to 40, one number per line, nothing else.", "Then end with the single word banana.", interrupt: false);
            await TurnAsync(session, events, interrupted, "Count from 1 to 60, one number per line, nothing else.", "Then end with the single word cherry.", interrupt: true);
            await TurnAsync(session, events, after, "Reply with the single word ok.", string.Empty, interrupt: false);
        }

        await SaveTranscriptsAsync(transcripts, "mid-turn", repository.Path);
        await SaveCostAsync("mid-turn", [.. steered, .. interrupted, .. after]);

        Assert.Single(steered.OfType<TurnCompleted>());
        Assert.Contains(steered, agentEvent => agentEvent is MessageQueued);
        Assert.Contains("banana", string.Concat(steered.OfType<ItemProgressed>().Select(progressed => progressed.Text)), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TurnOutcome.Interrupted, interrupted.OfType<TurnCompleted>().Single().Outcome);
        Assert.Equal(TurnOutcome.Finished, after.OfType<TurnCompleted>().Single().Outcome);
        Assert.Contains("ok", string.Concat(after.OfType<ItemProgressed>().Select(progressed => progressed.Text)), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cherry", string.Concat(after.OfType<ItemProgressed>().Select(progressed => progressed.Text)), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task TurnAsync(IAgentSession session, IAsyncEnumerator<IAgentEvent> events, List<IAgentEvent> seen, string instruction, string steering, bool interrupt)
    {
        var turn = (await session.SendAsync(new UserTurn(instruction), Cancellation)).Match(started => started, error => throw new InvalidOperationException(error.ToString()));
        var steeredYet = steering.Length == 0;

        while (await events.MoveNextAsync())
        {
            var agentEvent = events.Current;
            seen.Add(agentEvent);

            if (!steeredYet && agentEvent is ItemProgressed)
            {
                steeredYet = true;
                _ = (await session.SendAsync(new UserTurn(steering) { MidTurn = true }, Cancellation)).Match(joined => joined, error => throw new InvalidOperationException(error.ToString()));

                if (interrupt)
                {
                    _ = (await session.InterruptAsync(Cancellation)).Match(stopped => stopped, error => throw new InvalidOperationException(error.ToString()));
                }
            }

            if (agentEvent is TurnCompleted completed && completed.Turn == turn)
            {
                return;
            }
        }
    }

    [Fact]
    public async Task TheRepositorysAllowRuleAndHooksNeverAnswerForAvalaAsync()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Gate) == "1", $"Set {Gate}=1 to run real Claude Code sessions.");
        using var data = new TemporaryFolder();
        using var repository = new TemporaryFolder();
        using var markers = new TemporaryFolder();
        var marker = Path.Combine(markers.Path, "hook-ran");
        Directory.CreateDirectory(Path.Combine(repository.Path, ".claude"));
        await File.WriteAllTextAsync(
            Path.Combine(repository.Path, ".claude", "settings.json"),
            new JsonObject
            {
                ["permissions"] = new JsonObject { ["allow"] = new JsonArray("Bash(ls:*)"), ["defaultMode"] = "bypassPermissions" },
                ["hooks"] = new JsonObject
                {
                    ["PreToolUse"] = new JsonArray(new JsonObject
                    {
                        ["matcher"] = "*",
                        ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = $"touch '{marker}'" }),
                    }),
                },
            }.ToJsonString(),
            Cancellation);
        await using var root = CompositionRoot.Create(plugins.Directory, new AvalaPaths(data.Path));
        var provider = root.Services.GetServices<IAgentProvider>().Single(candidate => candidate.Info.Id == "claude-code");
        var transcripts = Path.Combine(data.Path, "transcripts");
        const string Instruction = "Run the command `ls` with the Bash tool, then say done. Be brief.";

        var hooksOff = await TalkAsync(provider, new SessionOptions(repository.Path, PermissionMode.AskEveryTime) { Connection = Connection(Login, Path.Combine(transcripts, "hooks-off")) }, Instruction);
        var ranWithHooksOff = File.Exists(marker);
        var hooksOn = await TalkAsync(
            provider,
            new SessionOptions(repository.Path, PermissionMode.AskEveryTime) { Connection = Connection(Login, Path.Combine(transcripts, "hooks-on"), ("userHooks", "true")) },
            Instruction);

        await SaveTranscriptsAsync(transcripts, "user-configuration", repository.Path);
        await SaveCostAsync("user-configuration", [.. hooksOff, .. hooksOn]);

        Assert.All(new[] { hooksOff, hooksOn }, events => Assert.Contains(events, agentEvent => agentEvent is PermissionRequested { Kind: ItemKind.Command } asked && asked.Target.StartsWith("ls", StringComparison.Ordinal)));
        Assert.False(ranWithHooksOff);
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task TheUsersMcpServersRunInTheSessionsProcessTreeAndEndWithItAsync()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Gate) == "1", $"Set {Gate}=1 to start the real Claude Code CLI.");
        using var data = new TemporaryFolder();
        using var repository = new TemporaryFolder();
        using var login = new TemporaryFolder();
        var started = Path.Combine(login.Path, "server.pid");
        await File.WriteAllTextAsync(
            Path.Combine(login.Path, ".claude.json"),
            new JsonObject
            {
                ["mcpServers"] = new JsonObject
                {
                    ["sleeper"] = new JsonObject
                    {
                        ["type"] = "stdio",
                        ["command"] = "sh",
                        ["args"] = new JsonArray("-c", $"echo $$ > '{started}.tmp' && mv '{started}.tmp' '{started}' && exec sleep 600"),
                    },
                },
            }.ToJsonString(),
            Cancellation);
        using var watcher = new FileSystemWatcher(login.Path, "server.pid") { NotifyFilter = NotifyFilters.FileName };
        var serverStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Renamed += (_, _) => serverStarted.TrySetResult();
        watcher.EnableRaisingEvents = true;
        await using var root = CompositionRoot.Create(plugins.Directory, new AvalaPaths(data.Path));
        var provider = root.Services.GetServices<IAgentProvider>().Single(candidate => candidate.Info.Id == "claude-code");
        var trees = root.Services.GetRequiredService<IProcessTrees>();
        var tree = await trees.OpenAsync(repository.Path, Cancellation);

        var session = (await provider.StartAsync(
                new SessionOptions(repository.Path, PermissionMode.AskEveryTime) { Connection = new ConnectionEnvironment { ConfigurationDirectory = login.Path }, Processes = tree },
                Cancellation))
            .Match(opened => opened, error => throw new InvalidOperationException(error.ToString()));
        await session.SendAsync(new UserTurn("Say hello."), Cancellation);
        await serverStarted.Task.WaitAsync(TimeSpan.FromMinutes(1), Cancellation);
        var server = int.Parse((await File.ReadAllTextAsync(started, Cancellation)).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        var members = await tree.MembersAsync(Cancellation);
        await session.DisposeAsync();
        var survivors = await trees.CloseAsync(tree.Id, Cancellation);

        Assert.Contains(members, member => member.Id == server);
        Assert.Empty(survivors);
        Assert.False(Directory.Exists($"/proc/{server}"));
    }

    private async Task JobAsync(string name, string instruction, string permissions, IReadOnlyList<string> files, params (string Path, string Content)[] committed)
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable(Gate) == "1", $"Set {Gate}=1 to run real Claude Code sessions.");
        var fixture = new RegressionFixture(null, [(".avala/permissions.json", permissions), .. committed], files, new Outcome([], [], [], [], []));
        var connections = new JsonObject
        {
            ["connections"] = new JsonArray(new JsonObject
            {
                ["name"] = "claude",
                ["provider"] = "claude-code",
                ["credential"] = new JsonObject { ["source"] = "login", ["reference"] = Login },
                ["settings"] = new JsonObject { ["model"] = Model, ["transcripts"] = "${transcripts}" },
            }),
        };

        await using var run = await SimulatedRun.RealAsync(
            plugins,
            instruction,
            [
                ("connections.json", connections.ToJsonString().Replace("${transcripts}", JsonEncodedText.Encode(TranscriptsOf(name)).ToString(), StringComparison.Ordinal)),
                ("recording.json", new JsonObject { ["enabled"] = true, ["redact"] = new JsonArray([.. (await SecretsAsync()).Select(secret => JsonValue.Create(secret))]) }.ToJsonString()),
            ],
            fixture.Committed);
        var outcome = await RecordedSessionTests.OutcomeAsync(run, fixture);
        var worktree = run.Worktree;
        var recording = Assert.Single(await run.StopAndReadRecordingsAsync());

        Directory.CreateDirectory(Output);
        await File.WriteAllTextAsync(Path.Combine(Output, $"{name}.json"), recording, Cancellation);
        await File.WriteAllTextAsync(Path.Combine(Output, $"{name}.expected.json"), Expectations(fixture, outcome), Cancellation);
        await SaveTranscriptsAsync(TranscriptsOf(name), name, worktree);
        await SaveCostAsync(name, Spent(recording));
    }

    private static string TranscriptsOf(string name) => Path.Combine(Output, "raw", name);

    private static async Task<IReadOnlyList<IAgentEvent>> TalkAsync(IAgentProvider provider, SessionOptions options, string instruction)
    {
        var events = new List<IAgentEvent>();
        var session = (await provider.StartAsync(options, Cancellation)).Match(started => started, error => throw new InvalidOperationException(error.ToString()));

        await using (session)
        {
            _ = (await session.SendAsync(new UserTurn(instruction), Cancellation)).Match(turn => turn, error => throw new InvalidOperationException(error.ToString()));

            await foreach (var agentEvent in session.Events.WithCancellation(Cancellation))
            {
                events.Add(agentEvent);

                switch (agentEvent)
                {
                    case PermissionRequested requested:
                        await session.RespondAsync(new PermissionDecision(requested.Item, PermissionAnswer.Allow), Cancellation);
                        break;
                    case ToolCalled toolCalled:
                        await session.ReturnAsync(new ToolResult(toolCalled.Item, "The follow-up is proposed."), Cancellation);
                        break;
                    case TurnCompleted:
                        return events;
                }
            }
        }

        return events;
    }

    private static ConnectionEnvironment Connection(string login, string transcripts, params (string Name, string Value)[] settings) =>
        new()
        {
            ConfigurationDirectory = login,
            Settings = new Dictionary<string, string>(settings.Select(setting => KeyValuePair.Create(setting.Name, setting.Value))) { ["model"] = Model, ["transcripts"] = transcripts },
        };

    private static string Expectations(RegressionFixture fixture, Outcome outcome) =>
        new JsonObject
        {
            ["repository"] = new JsonObject([.. fixture.Committed.Select(file => KeyValuePair.Create(file.Path, (JsonNode?)JsonValue.Create(file.Content)))]),
            ["journey"] = Array(outcome.Journey),
            ["permissions"] = Array(outcome.Permissions),
            ["forms"] = Array(outcome.Forms),
            ["verifications"] = Array(outcome.Verifications),
            ["files"] = new JsonObject([.. outcome.Files.Select(file => file.Split(": ", 2)).Select(file => KeyValuePair.Create(file[0], (JsonNode?)JsonValue.Create(file[1])))]),
        }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    private static JsonArray Array(IEnumerable<string> values) => [.. values.Select(value => JsonValue.Create(value))];

    private static decimal Spent(string recording) =>
        (JsonNode.Parse(recording)?["entries"]?.AsArray() ?? [])
            .Select(entry => entry?["event"])
            .Where(recorded => recorded?["type"]?.GetValue<string>() == "usageReported")
            .Sum(recorded => recorded?["cost"]?["amount"]?.GetValue<decimal>() ?? 0m);

    private static Task SaveCostAsync(string name, IEnumerable<IAgentEvent> events) =>
        SaveCostAsync(name, events.OfType<UsageReported>().Sum(reported => reported.Cost.Match(cost => cost.Amount, () => 0m)));

    private static async Task SaveCostAsync(string name, decimal spent)
    {
        Directory.CreateDirectory(Output);
        await File.AppendAllTextAsync(Path.Combine(Output, "cost.txt"), $"{name} {spent}\n", Cancellation);
    }

    private static async Task SaveTranscriptsAsync(string folder, string name, string workingDirectory)
    {
        var target = Directory.CreateDirectory(Path.Combine(Output, "transcripts", name)).FullName;
        var secrets = await SecretsAsync();

        foreach (var file in Directory.Exists(folder) ? Directory.GetFiles(folder, "*.jsonl", SearchOption.AllDirectories) : [])
        {
            var text = await File.ReadAllTextAsync(file, Cancellation);
            var relative = Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '-');
            await File.WriteAllTextAsync(Path.Combine(target, relative), Redacted(text, workingDirectory, secrets), Cancellation);
        }
    }

    private static string Redacted(string text, string workingDirectory, IReadOnlyList<string> secrets)
    {
        var redacted = text
            .Replace(JsonEncodedText.Encode(workingDirectory).ToString(), "${workingDirectory}", StringComparison.Ordinal)
            .Replace(workingDirectory, "${workingDirectory}", StringComparison.Ordinal);

        return secrets.Aggregate(redacted, (current, secret) => current.Replace(secret, "[redacted]", StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyList<string>> SecretsAsync()
    {
        var secrets = new List<string> { Home };

        foreach (var global in new[] { Path.Combine(Login, ".claude.json"), Path.Combine(SecondLogin, ".claude.json"), Path.Combine(Home, ".claude.json") })
        {
            if (!File.Exists(global))
            {
                continue;
            }

            var account = JsonNode.Parse(await File.ReadAllTextAsync(global, Cancellation))?["oauthAccount"];

            secrets.AddRange(AccountFields
                .Select(field => account?[field]?.GetValue<string>())
                .OfType<string>()
                .Where(value => value.Length > 3));
        }

        secrets.Add(Environment.UserName);

        return [.. secrets.Distinct().OrderByDescending(secret => secret.Length)];
    }
}
