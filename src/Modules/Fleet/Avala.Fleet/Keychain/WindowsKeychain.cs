using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Avala.Fleet.Contracts;
using Avala.Sdk;

namespace Avala.Fleet.Keychain;

[SupportedOSPlatform("windows")]
internal sealed class WindowsKeychain : IKeychain
{
    private const uint Generic = 1;

    private const uint LocalMachine = 2;

    private const int NotFound = 1168;

    public ValueTask<bool> AvailableAsync(CancellationToken cancellationToken) => ValueTask.FromResult(OperatingSystem.IsWindows());

    public async ValueTask<Result<string, KeychainFailure>> StoreAsync(string account, string secret, CancellationToken cancellationToken) =>
        OperatingSystem.IsWindows()
            ? await PlatformKeychain.OffThreadAsync(() => Store(account, secret), cancellationToken)
            : KeychainFailure.Unavailable;

    public async ValueTask<Result<Option<string>, KeychainFailure>> ReadAsync(string account, CancellationToken cancellationToken) =>
        OperatingSystem.IsWindows()
            ? await PlatformKeychain.OffThreadAsync(() => Read(account), cancellationToken)
            : KeychainFailure.Unavailable;

    public async ValueTask<Result<string, KeychainFailure>> RemoveAsync(string account, CancellationToken cancellationToken) =>
        OperatingSystem.IsWindows()
            ? await PlatformKeychain.OffThreadAsync(() => Remove(account), cancellationToken)
            : KeychainFailure.Unavailable;

    private static string Target(string account) => $"{PlatformKeychain.Service}:{account}";

    private static Result<string, KeychainFailure> Store(string account, string secret)
    {
        var blob = Encoding.UTF8.GetBytes(secret);
        var target = Marshal.StringToCoTaskMemUni(Target(account));
        var user = Marshal.StringToCoTaskMemUni(account);
        var data = Marshal.AllocCoTaskMem(blob.Length);

        try
        {
            Marshal.Copy(blob, 0, data, blob.Length);
            var credential = new Credential
            {
                Type = Generic,
                TargetName = target,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = data,
                Persist = LocalMachine,
                UserName = user,
            };

            return CredWrite(ref credential, 0) ? account : KeychainFailure.Failed;
        }
        finally
        {
            Marshal.Copy(new byte[blob.Length], 0, data, blob.Length);
            Marshal.FreeCoTaskMem(data);
            Marshal.FreeCoTaskMem(target);
            Marshal.FreeCoTaskMem(user);
            Array.Clear(blob);
        }
    }

    private static Result<Option<string>, KeychainFailure> Read(string account)
    {
        if (!CredRead(Target(account), Generic, 0, out var found))
        {
            return Marshal.GetLastPInvokeError() == NotFound ? Option<string>.None : KeychainFailure.Failed;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(found);
            var blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            var secret = Encoding.UTF8.GetString(blob);
            Array.Clear(blob);

            return Option<string>.Some(secret);
        }
        finally
        {
            CredFree(found);
        }
    }

    private static Result<string, KeychainFailure> Remove(string account) =>
        CredDelete(Target(account), Generic, 0) || Marshal.GetLastPInvokeError() == NotFound ? account : KeychainFailure.Failed;

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
