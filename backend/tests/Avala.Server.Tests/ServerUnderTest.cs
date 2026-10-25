using Ardalis.Result;
using Avala.Server.Instances.Contracts.Authentication;
using Avala.Server.Testing;
using Mediator;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Server.Tests;

public sealed class ServerUnderTest : WebApplicationFactory<Program>
{
    public const string AdministratorKey = "an-administrator-key-for-the-host-tests";

    private static readonly AuthenticatedInstance Desktop = new(Guid.Parse("0199f6a0-0000-7000-8000-000000000003"), "desktop");

    public string ConfiguredKey { get; init; } = AdministratorKey;

    public bool AppliesMigrations { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Authentication:AdministratorKey", ConfiguredKey);
        builder.UseSetting("Database:ApplyMigrations", AppliesMigrations ? "true" : "false");
        builder.UseSetting("ConnectionStrings:fleet", "Host=127.0.0.1;Port=1;Timeout=1");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISender>();
            services.AddScoped<ISender>(_ => new FakeSender(message => message is AuthenticateInstance
                ? Result<AuthenticatedInstance>.Success(Desktop)
                : Result.Success()));
        });
    }
}
