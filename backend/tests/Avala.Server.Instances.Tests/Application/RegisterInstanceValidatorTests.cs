using Avala.Server.Instances.Application.Registration;
using Avala.Server.Instances.Contracts.Registration;

namespace Avala.Server.Instances.Tests.Application;

public sealed class RegisterInstanceValidatorTests
{
    private readonly RegisterInstanceValidator validator = new();

    [Fact]
    public void ANameIsRequired() =>
        Assert.False(validator.Validate(new RegisterInstance(string.Empty)).IsValid);

    [Fact]
    public void ANameLongerThanAHundredCharactersIsRefused() =>
        Assert.False(validator.Validate(new RegisterInstance(new string('a', 101))).IsValid);
}
