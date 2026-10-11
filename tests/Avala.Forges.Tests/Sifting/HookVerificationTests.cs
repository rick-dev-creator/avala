using System.Text;
using Avala.Forges.Contracts;
using Avala.Forges.Sifting;
using Avala.Triggers.Contracts;

namespace Avala.Forges.Tests.Sifting;

public sealed class HookVerificationTests
{
    private const string Secret = "s3cret";

    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{ "action": "labeled" }""");

    private static readonly HookSignature GitHub = new(["X-Hub-Signature-256"], "sha256=");

    private static readonly HookSignature Forgejo = new(["X-Forgejo-Signature", "X-Gitea-Signature"], string.Empty);

    [Fact]
    public void ABodySignedWithTheSecretUnderTheForgesHeaderIsAccepted()
    {
        Assert.Equal(DeliveryVerdict.Accepted, HookVerification.Check(GitHub, Headers("x-hub-signature-256", HookVerification.Sign(GitHub, Secret, Body)), Body, Secret));
        Assert.Equal(DeliveryVerdict.Accepted, HookVerification.Check(Forgejo, Headers("X-Gitea-Signature", HookVerification.Sign(Forgejo, Secret, Body)), Body, Secret));
    }

    [Fact]
    public void NoSignatureHeaderIsNotSignedAndAnotherSecretBodyOrPrefixIsABadSignature()
    {
        var signed = HookVerification.Sign(GitHub, Secret, Body);

        Assert.Equal(DeliveryVerdict.NotSigned, HookVerification.Check(GitHub, Headers("X-Gitea-Signature", signed), Body, Secret));
        Assert.Equal(DeliveryVerdict.BadSignature, HookVerification.Check(GitHub, Headers("X-Hub-Signature-256", HookVerification.Sign(GitHub, "other", Body)), Body, Secret));
        Assert.Equal(DeliveryVerdict.BadSignature, HookVerification.Check(GitHub, Headers("X-Hub-Signature-256", signed), Encoding.UTF8.GetBytes("{}"), Secret));
        Assert.Equal(DeliveryVerdict.BadSignature, HookVerification.Check(GitHub, Headers("X-Hub-Signature-256", signed["sha256=".Length..]), Body, Secret));
        Assert.Equal(DeliveryVerdict.BadSignature, HookVerification.Check(GitHub, Headers("X-Hub-Signature-256", "sha256=zz"), Body, Secret));
    }

    private static Dictionary<string, string> Headers(string name, string value) => new() { [name] = value };
}
