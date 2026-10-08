namespace Avala.Fixtures.Violating.Application;

public sealed class OverloadedService(
    LedgerService ledger,
    TimeProvider clock,
    IServiceProvider services,
    IFormatProvider format,
    IComparer<string> comparer)
{
    public string Describe() => $"{ledger.Name} {clock.GetUtcNow()} {services} {format} {comparer}";
}
