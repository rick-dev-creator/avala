using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Conversations;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.ClaudeCode.Tests.Conversations;

internal sealed class FakeCli : ICli
{
    public List<CliLaunch> Launches { get; } = [];

    public Channel<string> Output { get; } = Channel.CreateUnbounded<string>();

    public Channel<string> Written { get; } = Channel.CreateUnbounded<string>();

    public AgentError? Refusal { get; init; }

    public bool StdinClosed { get; set; }

    public Result<ICliProcess, AgentError> Start(CliLaunch launch, IProcessLauncher processes, Option<string> transcripts)
    {
        if (Refusal is { } refusal)
        {
            return refusal;
        }

        Launches.Add(launch);

        return new Process(this);
    }

    private sealed class Process(FakeCli cli) : ICliProcess
    {
        public IAsyncEnumerable<string> Lines => ReadAsync(CancellationToken.None);

        public Task WriteAsync(string line, CancellationToken cancellationToken) =>
            cli.StdinClosed ? Task.FromException(new IOException("The pipe is closed.")) : cli.Written.Writer.WriteAsync(line, cancellationToken).AsTask();

        public ValueTask DisposeAsync()
        {
            cli.Output.Writer.TryComplete();

            return ValueTask.CompletedTask;
        }

        private async IAsyncEnumerable<string> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var line in cli.Output.Reader.ReadAllAsync(cancellationToken))
            {
                yield return line;
            }
        }
    }
}
