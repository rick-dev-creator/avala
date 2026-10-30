using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Avala.Server.Instances.Tests.Fakes;

internal sealed class FakeSchemeOptions : IOptionsMonitor<AuthenticationSchemeOptions>
{
    public AuthenticationSchemeOptions CurrentValue { get; } = new();

    public AuthenticationSchemeOptions Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
}
