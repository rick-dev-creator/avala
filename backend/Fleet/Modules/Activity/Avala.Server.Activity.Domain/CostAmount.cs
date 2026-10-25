namespace Avala.Server.Activity.Domain;

internal sealed record CostAmount(decimal Amount, string Currency, CostBasis Basis)
{
    public const decimal Maximum = 1_000_000m;

    public const int Decimals = 6;

    public static Admission<CostAmount> Create(decimal amount, string currency, CostBasis basis) =>
        amount is < 0 or > Maximum ? Admission.Refuse<CostAmount>($"a cost is between 0 and {Maximum}")
        : decimal.Round(amount, Decimals) != amount ? Admission.Refuse<CostAmount>($"a cost has at most {Decimals} decimals")
        : currency is not { Length: 3 } || !currency.All(char.IsAsciiLetterUpper) ? Admission.Refuse<CostAmount>("a currency is three uppercase letters")
        : !Enum.IsDefined(basis) ? Admission.Refuse<CostAmount>("the cost basis is unknown")
        : Admission.Admit(new CostAmount(amount, currency, basis));
}
