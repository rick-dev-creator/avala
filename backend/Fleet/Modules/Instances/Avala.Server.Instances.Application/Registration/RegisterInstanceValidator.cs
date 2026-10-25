using Avala.Server.Instances.Contracts.Registration;
using Avala.Server.Instances.Domain;
using FluentValidation;

namespace Avala.Server.Instances.Application.Registration;

internal sealed class RegisterInstanceValidator : AbstractValidator<RegisterInstance>
{
    public RegisterInstanceValidator() =>
        RuleFor(command => command.Name).NotEmpty().MaximumLength(Instance.NameLength);
}
