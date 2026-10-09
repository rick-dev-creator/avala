using System.Globalization;

namespace Avala.Fixtures.Compliant.Domain;

public readonly record struct Money(decimal Amount, string Currency)
{
    public string Label => string.Join(" ", Amount.ToString(CultureInfo.InvariantCulture), Currency);
}
