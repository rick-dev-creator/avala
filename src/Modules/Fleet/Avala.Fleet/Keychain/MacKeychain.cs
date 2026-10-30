using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Avala.Fleet.Contracts;
using Avala.Sdk;
using static Avala.Fleet.Keychain.MacStatus;

namespace Avala.Fleet.Keychain;

[SupportedOSPlatform("macos")]
internal sealed class MacKeychain : IKeychain
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";

    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private static readonly byte[] ServiceName = Encoding.UTF8.GetBytes(PlatformKeychain.Service);

    public ValueTask<bool> AvailableAsync(CancellationToken cancellationToken) => ValueTask.FromResult(OperatingSystem.IsMacOS());

    public async ValueTask<Result<string, KeychainFailure>> StoreAsync(string account, string secret, CancellationToken cancellationToken) =>
        OperatingSystem.IsMacOS()
            ? await PlatformKeychain.OffThreadAsync(() => Store(account, secret), cancellationToken)
            : KeychainFailure.Unavailable;

    public async ValueTask<Result<Option<string>, KeychainFailure>> ReadAsync(string account, CancellationToken cancellationToken) =>
        OperatingSystem.IsMacOS()
            ? await PlatformKeychain.OffThreadAsync(() => Read(account), cancellationToken)
            : KeychainFailure.Unavailable;

    public async ValueTask<Result<string, KeychainFailure>> RemoveAsync(string account, CancellationToken cancellationToken) =>
        OperatingSystem.IsMacOS()
            ? await PlatformKeychain.OffThreadAsync(() => Remove(account), cancellationToken)
            : KeychainFailure.Unavailable;

    private static Result<string, KeychainFailure> Store(string account, string secret)
    {
        var name = Encoding.UTF8.GetBytes(account);
        var password = Encoding.UTF8.GetBytes(secret);

        try
        {
            var found = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)ServiceName.Length, ServiceName, (uint)name.Length, name, out var length, out var data, out var item);

            if (found == Success)
            {
                _ = SecKeychainItemFreeContent(IntPtr.Zero, data);
                var modified = SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, (uint)password.Length, password);
                CFRelease(item);

                return modified == Success && length >= 0 ? account : Failure(modified);
            }

            if (found != NotFound)
            {
                return Failure(found);
            }

            var added = SecKeychainAddGenericPassword(IntPtr.Zero, (uint)ServiceName.Length, ServiceName, (uint)name.Length, name, (uint)password.Length, password, IntPtr.Zero);

            return added == Success ? account : Failure(added);
        }
        finally
        {
            Array.Clear(password);
        }
    }

    private static Result<Option<string>, KeychainFailure> Read(string account)
    {
        var name = Encoding.UTF8.GetBytes(account);
        var found = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)ServiceName.Length, ServiceName, (uint)name.Length, name, out var length, out var data, out var item);

        if (found == NotFound)
        {
            return Option<string>.None;
        }

        if (found != Success)
        {
            return Failure(found);
        }

        try
        {
            var password = new byte[length];
            Marshal.Copy(data, password, 0, password.Length);
            var secret = Encoding.UTF8.GetString(password);
            Array.Clear(password);

            return Option<string>.Some(secret);
        }
        finally
        {
            _ = SecKeychainItemFreeContent(IntPtr.Zero, data);
            CFRelease(item);
        }
    }

    private static Result<string, KeychainFailure> Remove(string account)
    {
        var name = Encoding.UTF8.GetBytes(account);
        var found = SecKeychainFindGenericPassword(IntPtr.Zero, (uint)ServiceName.Length, ServiceName, (uint)name.Length, name, out _, out var data, out var item);

        if (found == NotFound)
        {
            return account;
        }

        if (found != Success)
        {
            return Failure(found);
        }

        _ = SecKeychainItemFreeContent(IntPtr.Zero, data);
        var deleted = SecKeychainItemDelete(item);
        CFRelease(item);

        return deleted == Success ? account : Failure(deleted);
    }

    [DllImport(Security)]
    private static extern int SecKeychainAddGenericPassword(
        IntPtr keychain,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        uint passwordLength,
        byte[] passwordData,
        IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainFindGenericPassword(
        IntPtr keychainOrArray,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        out uint passwordLength,
        out IntPtr passwordData,
        out IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemModifyAttributesAndData(IntPtr itemRef, IntPtr attributeList, uint length, byte[] data);

    [DllImport(Security)]
    private static extern int SecKeychainItemDelete(IntPtr itemRef);

    [DllImport(Security)]
    private static extern int SecKeychainItemFreeContent(IntPtr attributeList, IntPtr data);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr reference);
}
