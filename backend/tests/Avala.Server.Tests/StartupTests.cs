using Microsoft.Extensions.Options;

namespace Avala.Server.Tests;

public sealed class StartupTests
{
    [Fact]
    public async Task ABadAdministratorKeyStopsTheServerBeforeTheDatabaseAsync()
    {
        await using var server = new ServerUnderTest { ConfiguredKey = string.Empty, AppliesMigrations = true };

        var failure = Assert.ThrowsAny<Exception>(() => server.CreateClient());

        Assert.Contains("Authentication:AdministratorKey", Assert.IsType<OptionsValidationException>(Innermost(failure)).Message, StringComparison.Ordinal);
    }

    private static Exception Innermost(Exception exception) =>
        exception is AggregateException { InnerExceptions: [var only] } ? Innermost(only)
        : exception.InnerException is { } inner && exception is not OptionsValidationException ? Innermost(inner)
        : exception;
}
