namespace Avala.Sdk.Processes;

public sealed record ProcessOutcome(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;
}
