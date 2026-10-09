using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Conversations;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.ClaudeCode.Cli;

internal sealed class ProcessCli(CliCommand command, TimeProvider clock) : ICli
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
            process => Result<ICliProcess, AgentError>.Success(new LaunchedCli(process, transcripts.Map(folder => TranscriptTap.Open(folder, launch, clock)))),
            _ => AgentError.ProviderUnavailable);
    }

    private sealed class LaunchedCli : ICliProcess
    {
        private readonly Process process;
        private readonly Option<TranscriptTap> tap;
        private readonly Task errors;

        public LaunchedCli(Process process, Option<TranscriptTap> tap)
        {
            this.process = process;
            this.tap = tap;
            errors = process.StandardError.ReadToEndAsync();
        }

        public IAsyncEnumerable<string> Lines => ReadAsync(CancellationToken.None);

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
            await tap.Match(transcript => transcript.DisposeAsync().AsTask(), () => Task.CompletedTask);
            process.Dispose();
        }

        private async IAsyncEnumerable<string> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                Tap(TranscriptTap.Output, line);

                yield return line;
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
