namespace Avala.Server.Instances.EndPoints.Authentication;

internal sealed class AdministratorSettings
{
    public const string Section = "Authentication";

    public const string Setting = Section + ":" + nameof(AdministratorKey);

    public const int MinimumLength = 32;

    public string AdministratorKey { get; init; } = string.Empty;

    public const string Masked = "***";

    public override string ToString() => $"{nameof(AdministratorSettings)} {{ AdministratorKey = {Masked} }}";
}
