namespace Avala.Server.Activity.Domain;

internal sealed class TokenTotals
{
    public decimal Input { get; private set; }

    public decimal Output { get; private set; }

    public decimal CacheRead { get; private set; }

    public decimal CacheWrite { get; private set; }

    public decimal Reasoning { get; private set; }

    public void Add(TokenCounts counts)
    {
        Input += counts.Input;
        Output += counts.Output;
        CacheRead += counts.CacheRead;
        CacheWrite += counts.CacheWrite;
        Reasoning += counts.Reasoning;
    }
}
