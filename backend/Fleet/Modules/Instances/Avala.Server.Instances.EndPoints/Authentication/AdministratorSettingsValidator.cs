using Microsoft.Extensions.Options;

namespace Avala.Server.Instances.EndPoints.Authentication;

internal sealed class AdministratorSettingsValidator : IValidateOptions<AdministratorSettings>
{
    public ValidateOptionsResult Validate(string? name, AdministratorSettings options) =>
        options.AdministratorKey.Length switch
        {
            0 => ValidateOptionsResult.Fail(
                $"{AdministratorSettings.Setting} is not set. Set it to a secret of at least {AdministratorSettings.MinimumLength} characters, such as the output of `openssl rand -base64 32`."),
            < AdministratorSettings.MinimumLength => ValidateOptionsResult.Fail(
                $"{AdministratorSettings.Setting} is shorter than {AdministratorSettings.MinimumLength} characters."),
            _ => ValidateOptionsResult.Success,
        };
}
