using Avala.Fleet.Contracts;

namespace Avala.Fleet.Keychain;

internal static class MacStatus
{
    public const int Success = 0;

    public const int NotFound = -25300;

    private const int UserCanceled = -128;

    private const int AuthFailed = -25293;

    private const int InteractionNotAllowed = -25308;

    public static KeychainFailure Failure(int status) =>
        status is UserCanceled or AuthFailed or InteractionNotAllowed ? KeychainFailure.Denied : KeychainFailure.Failed;
}
