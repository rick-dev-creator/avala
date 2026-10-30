namespace Avala.Server.Activity.Domain;

internal enum VerificationOutcome
{
    Passed,
    Failed,
    NoChecksDeclared,
    InvalidDeclaration,
}
