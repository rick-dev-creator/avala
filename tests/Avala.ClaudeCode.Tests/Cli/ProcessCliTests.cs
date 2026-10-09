using Avala.ClaudeCode.Cli;
using Avala.ClaudeCode.Conversations;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Microsoft.Extensions.Logging;

namespace Avala.ClaudeCode.Tests.Cli;

public sealed class ProcessCliTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WhatTheProcessWritesToItsStandardErrorIsLoggedLineByLineAsync()
    {
        await using var folder = new TemporaryFolder();
        var logger = new RecordingLogger();
        var cli = Outcomes.Succeeds(new ProcessCli(new CliCommand("git", []), TimeProvider.System, logger)
            .Start(new CliLaunch(folder.Path, ["--no-such-option"], [], new Dictionary<string, string>()), UncontainedProcesses.Instance, Option<string>.None));

        await using (cli)
        {
            await foreach (var _ in cli.Lines.WithCancellation(Cancellation))
            {
            }
        }

        Assert.Contains((LogLevel.Warning, "Claude Code wrote to its standard error: unknown option: --no-such-option"), logger.Entries);
    }

    private sealed class RecordingLogger : ILogger<ProcessCli>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
