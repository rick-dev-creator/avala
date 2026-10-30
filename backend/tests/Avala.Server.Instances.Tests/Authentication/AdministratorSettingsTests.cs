using Avala.Server.Instances.EndPoints.Authentication;

namespace Avala.Server.Instances.Tests.Authentication;

public sealed class AdministratorSettingsTests
{
    private static readonly AdministratorSettingsValidator Validator = new();

    [Fact]
    public void AnEmptyAdministratorKeyIsRefused() =>
        Assert.Contains(AdministratorSettings.Setting, Validator.Validate(null, new AdministratorSettings()).FailureMessage, StringComparison.Ordinal);

    [Fact]
    public void AnAdministratorKeyShorterThanThirtyTwoCharactersIsRefused() =>
        Assert.True(Validator.Validate(null, new AdministratorSettings { AdministratorKey = new string('k', 31) }).Failed);

    [Fact]
    public void AnAdministratorKeyOfThirtyTwoCharactersIsAccepted() =>
        Assert.True(Validator.Validate(null, new AdministratorSettings { AdministratorKey = new string('k', 32) }).Succeeded);

    [Fact]
    public void TheAdministratorSettingsNeverShowTheKey() =>
        Assert.DoesNotContain(
            Authenticating.AdministratorKey,
            new AdministratorSettings { AdministratorKey = Authenticating.AdministratorKey }.ToString(),
            StringComparison.Ordinal);
}
