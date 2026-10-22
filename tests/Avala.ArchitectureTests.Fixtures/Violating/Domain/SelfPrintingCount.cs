namespace Avala.Fixtures.Violating.Domain;

public sealed record SelfPrintingCount(int Value)
{
    public SelfPrintingCount Next => new(Value + 1);
}
