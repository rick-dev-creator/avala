using Avala.Autopilot.Contracts;
using Avala.Autopilot.Looping;
using Avala.Autopilot.Loops;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Autopilot.RepositoryFiles;

internal sealed class LoopLimitsReader(IBaseFiles files, AvalaPaths paths) : ILoopLimits
{
    public const string MachineFile = "autopilot.json";

    public async Task<Result<LoopLimits, AutopilotError>> OfRepositoryAsync(string repository, CancellationToken cancellationToken)
    {
        var machine = await MachineAsync(cancellationToken);
        var declared = (await files.ReadCurrentAsync(repository, AutopilotRulesReader.JobFile, cancellationToken)).Match(
            file => file.Content.Match(LoopLimitsParser.ParseJobFile, () => LoopLimitsDeclaration.None),
            _ => AutopilotError.Unreadable);

        return machine.Bind(ceiling => declared.Map(limits => limits.Tightening(ceiling.Setting(new LoopLimits()))));
    }

    private async Task<Result<LoopLimitsDeclaration, AutopilotError>> MachineAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(paths.Data, MachineFile);

        if (!File.Exists(path))
        {
            return LoopLimitsDeclaration.None;
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

            if (stream.Length > LoopLimitsParser.MaximumBytes)
            {
                return AutopilotError.TooLarge;
            }

            using var reader = new StreamReader(stream);

            return LoopLimitsParser.ParseMachine(await reader.ReadToEndAsync(cancellationToken));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AutopilotError.Unreadable;
        }
    }
}
