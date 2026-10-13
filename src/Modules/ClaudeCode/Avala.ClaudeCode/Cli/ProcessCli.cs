using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Conversations;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Microsoft.Extensions.Logging;

namespace Avala.ClaudeCode.Cli;

internal sealed partial class ProcessCli(CliCommand command, TimeProvider clock, ILogger<ProcessCli> logger) : ICli
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public Result<ICliProcess, AgentError> Start(CliLaunch launch, IProcessLauncher processes, Option<string> transcripts)
    {
        var info = new ProcessStartInfo(command.FileName)
        {
            WorkingDirectory = launch.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };

        foreach (var argument in command.Prefix.Concat(launch.Arguments))
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var name in launch.Cleared)
        {
            info.Environment.Remove(name);
        }

        foreach (var (name, value) in launch.Variables)
        {
            info.Environment[name] = value;
        }

        return processes.Start(info).Match(
            process => Result<ICliProcess, AgentError>.Success(new LaunchedCli(process, transcripts.Map(folder => TranscriptTap.Open(folder, launch, clock)), this)),
            _ => AgentError.ProviderUnavailable);
    }

    private async Task ReadErrorsAsync(StreamReader errors)
    {
        while (await errors.ReadLineAsync() is { } line)
        {
            if (line.Length > 0)
            {
                LogError(line);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Claude Code wrote to its standard error: {Line}")]
    private partial void LogError(string line);

    private sealed class LaunchedCli : ICliProcess
    {
        private readonly Channel<string> output = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleWriter = true });
        private readonly Process process;
        private readonly Option<TranscriptTap> tap;
        private readonly Task errors;
        private readonly Task reading;

        public LaunchedCli(Process process, Option<TranscriptTap> tap, ProcessCli cli)
        {
            this.process = process;
            this.tap = tap;
            errors = cli.ReadErrorsAsync(process.StandardError);
            reading = ReadOutputAsync(process.StandardOutput);
        }

        public IAsyncEnumerable<string> Lines => output.Reader.ReadAllAsync(CancellationToken.None);

        public async Task WriteAsync(string line, CancellationToken cancellationToken)
        {
            Tap(TranscriptTap.Input, line);
            await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                process.StandardInput.Close();
                process.Kill(entireProcessTree: true);
            }
            catch (Exception failure) when (failure is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            {
            }

            await process.WaitForExitAsync();
            await errors;
            await reading;
            await tap.Match(transcript => transcript.DisposeAsync().AsTask(), () => Task.CompletedTask);
            process.Dispose();
        }

        private async Task ReadOutputAsync(StreamReader reader)
        {
            try
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    Tap(TranscriptTap.Output, line);
                    output.Writer.TryWrite(line);
                }

                output.Writer.TryComplete();
            }
            catch (IOException failure)
            {
                output.Writer.TryComplete(failure);
            }
        }

        private void Tap(string direction, string line)
        {
            foreach (var transcript in tap.Match<TranscriptTap[]>(transcript => [transcript], () => []))
            {
                transcript.Note(direction, line);
            }
        }
    }
}
