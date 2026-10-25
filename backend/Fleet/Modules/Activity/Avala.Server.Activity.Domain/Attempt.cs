namespace Avala.Server.Activity.Domain;

internal sealed record Attempt(int Number, VerificationOutcome Outcome)
{
    public const int Maximum = 10_000;

    public static Admission<Attempt> Create(int number, VerificationOutcome outcome) =>
        number is < 1 or > Maximum ? Admission.Refuse<Attempt>($"an attempt is between 1 and {Maximum}")
        : !Enum.IsDefined(outcome) ? Admission.Refuse<Attempt>("the outcome is unknown")
        : Admission.Admit(new Attempt(number, outcome));
}
