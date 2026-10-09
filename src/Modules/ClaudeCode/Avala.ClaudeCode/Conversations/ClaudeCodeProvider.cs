using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;

namespace Avala.ClaudeCode.Conversations;

internal sealed class ClaudeCodeProvider(ICli cli, IConfigurationFolders folders, UserHome home) : IAgentProvider
{
    public const string Id = "claude-code";

    public ProviderInfo Info { get; } = new(Id, "Claude Code");

    private static readonly CapabilitySet Declared = CapabilitySet.Of(
        new StreamsPartialOutput(),
        new ExposesReasoning(),
        new Interruptible(),
        new Resumable(),
        new AcceptsTools([ToolSurface.Canvas, ToolSurface.Executed]),
        new AsksForms(),
        new ReportsUsage(),
        new ReportsCost(Telemetry.Currency));

    public CapabilitySet CapabilitiesOn(ConnectionEnvironment connection) =>
        connection.ApiKey.IsSome ? Declared : Declared.With(new ReportsLimits(Telemetry.SubscriptionWindows));

    public async ValueTask<Result<IAgentSession, AgentError>> StartAsync(SessionOptions options, CancellationToken cancellationToken)
    {
        var resumed = options.Resume.Bind(ConversationMark.Read);

        if (options.Resume.IsSome && !resumed.Match(mark => folders.Holds(options.Connection, mark.Session), () => false))
        {
            return AgentError.CannotResume;
        }

        var workingDirectory = Path.GetFullPath(options.WorkingDirectory);
        var transcripts = options.Connection.Settings.TryGetValue(CommandLine.TranscriptsSetting, out var folder) ? folder : Option<string>.None;
        var account = await folders.AccountAsync(options.Connection, cancellationToken);

        return await cli.Start(CommandLine.For(workingDirectory, options.Connection, resumed, home), options.Processes, transcripts).Match(
            async process => Result<IAgentSession, AgentError>.Success(await ClaudeCodeSession.OpenAsync(
                process,
                session => new Conversation(session, options, workingDirectory, resumed),
                account)),
            error => Task.FromResult(Result<IAgentSession, AgentError>.Failure(error)));
    }
}
