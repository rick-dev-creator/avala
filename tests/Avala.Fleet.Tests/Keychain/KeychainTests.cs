using Avala.Fleet.Contracts;
using Avala.Fleet.Keychain;
using Avala.Sdk;

namespace Avala.Fleet.Tests.Keychain;

public sealed class KeychainTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AKeyIsStoredReadReplacedAndRemoved()
    {
        var keychain = await AvailableAsync();
        var account = $"https://test-{Guid.NewGuid():N}.example";

        try
        {
            var first = await keychain.StoreAsync(account, "avk_first", Cancellation);
            var second = await keychain.StoreAsync(account, "avk_second", Cancellation);
            var read = await keychain.ReadAsync(account, Cancellation);
            var removed = await keychain.RemoveAsync(account, Cancellation);
            var afterRemoval = await keychain.ReadAsync(account, Cancellation);

            Assert.Equal(
                (true, true, "avk_second", true, true),
                (first.IsSuccess, second.IsSuccess, read.Match(found => found.Match(key => key, () => "none"), failure => failure.ToString()), removed.IsSuccess, afterRemoval.Match(found => found.IsNone, _ => false)));
        }
        finally
        {
            await keychain.RemoveAsync(account, CancellationToken.None);
        }
    }

    [Fact]
    public async Task AnUnknownAccountHasNoKey()
    {
        var keychain = await AvailableAsync();

        Assert.True((await keychain.ReadAsync($"https://unknown-{Guid.NewGuid():N}.example", Cancellation)).Match(found => found.IsNone, _ => false));
    }

    [Theory]
    [InlineData("g-dbus-error-quark", 2, "The name org.freedesktop.secrets was not provided by any .service files")]
    [InlineData("g-io-error-quark", 0, "Cannot autolaunch D-Bus without X11 $DISPLAY")]
    public void AnyProbeErrorMeansUnavailable(string domain, int code, string message) =>
        Assert.False(LinuxKeychain.IsAvailable(new LinuxKeychain.Lookup(Option<string>.None, new LinuxKeychain.SecretError(domain, code, message))));

    [Theory]
    [InlineData("g-io-error-quark", 19, KeychainFailure.Denied)]
    [InlineData("g-dbus-error-quark", 19, KeychainFailure.Failed)]
    [InlineData("g-io-error-quark", 1, KeychainFailure.Failed)]
    public void OnlyACancelledCallIsADismissedPrompt(string domain, int code, KeychainFailure expected) =>
        Assert.Equal(expected, LinuxKeychain.Failure(new LinuxKeychain.SecretError(domain, code, "the prompt was dismissed")).Match(failure => failure, () => KeychainFailure.Unavailable));

    [Fact]
    public void AProbeWithoutAnErrorMeansAvailable() =>
        Assert.True(LinuxKeychain.IsAvailable(new LinuxKeychain.Lookup(Option<string>.None, Option<LinuxKeychain.SecretError>.None)));

    private static async Task<Contracts.IKeychain> AvailableAsync()
    {
        var keychain = PlatformKeychain.Create();
        var available = await keychain.AvailableAsync(Cancellation);

        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Assert.True(available, "Windows and macOS always have a keychain, so this test must not skip there.");
        }

        Assert.SkipUnless(available, "No Secret Service answers on this Linux session; run the test under dbus-run-session with an unlocked gnome-keyring.");

        return keychain;
    }
}
