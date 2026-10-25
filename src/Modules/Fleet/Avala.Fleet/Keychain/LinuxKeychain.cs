using System.Runtime.InteropServices;
using Avala.Fleet.Contracts;
using Avala.Sdk;

namespace Avala.Fleet.Keychain;

internal sealed class LinuxKeychain : IKeychain
{
    private const string ProbeAccount = "avala.fleet.probe";

    private const int Cancelled = 19;

    private const string IoErrors = "g-io-error-quark";

    private readonly Lazy<Option<Secrets>> secrets = new(Secrets.Load);

    public async ValueTask<bool> AvailableAsync(CancellationToken cancellationToken) =>
        await secrets.Value.Match(
            loaded => PlatformKeychain.OffThreadAsync(() => IsAvailable(loaded.Lookup(ProbeAccount)), cancellationToken),
            () => Task.FromResult(false));

    public async ValueTask<Result<string, KeychainFailure>> StoreAsync(string account, string secret, CancellationToken cancellationToken) =>
        await secrets.Value.Match(
            loaded => PlatformKeychain.OffThreadAsync(() => Failure(loaded.Store(account, secret)).Match<Result<string, KeychainFailure>>(failure => failure, () => account), cancellationToken),
            () => Task.FromResult<Result<string, KeychainFailure>>(KeychainFailure.Unavailable));

    public async ValueTask<Result<Option<string>, KeychainFailure>> ReadAsync(string account, CancellationToken cancellationToken) =>
        await secrets.Value.Match(
            loaded => PlatformKeychain.OffThreadAsync(
                () =>
                {
                    var (secret, error) = loaded.Lookup(account);

                    return Failure(error).Match<Result<Option<string>, KeychainFailure>>(failure => failure, () => secret);
                },
                cancellationToken),
            () => Task.FromResult<Result<Option<string>, KeychainFailure>>(KeychainFailure.Unavailable));

    public async ValueTask<Result<string, KeychainFailure>> RemoveAsync(string account, CancellationToken cancellationToken) =>
        await secrets.Value.Match(
            loaded => PlatformKeychain.OffThreadAsync(() => Failure(loaded.Clear(account)).Match<Result<string, KeychainFailure>>(failure => failure, () => account), cancellationToken),
            () => Task.FromResult<Result<string, KeychainFailure>>(KeychainFailure.Unavailable));

    internal static bool IsAvailable(Lookup probe) => probe.Error.IsNone;

    internal static Option<KeychainFailure> Failure(Option<SecretError> error) =>
        error.Map(found => found.Domain == IoErrors && found.Code == Cancelled ? KeychainFailure.Denied : KeychainFailure.Failed);

    internal sealed record SecretError(string Domain, int Code, string Message);

    internal sealed record Lookup(Option<string> Secret, Option<SecretError> Error);

    private sealed class Secrets(IntPtr stringHash, IntPtr stringEqual)
    {
        private const string Glib = "libglib-2.0.so.0";

        private const string Library = "libsecret-1.so.0";

        public static Option<Secrets> Load() =>
            NativeLibrary.TryLoad(Glib, out var glib)
            && NativeLibrary.TryLoad(Library, out _)
            && NativeLibrary.TryGetExport(glib, "g_str_hash", out var hash)
            && NativeLibrary.TryGetExport(glib, "g_str_equal", out var equal)
                ? new Secrets(hash, equal)
                : Option<Secrets>.None;

        public Option<SecretError> Store(string account, string password) =>
            WithSchema(account, (schema, attributes) =>
            {
                var label = Marshal.StringToCoTaskMemUTF8($"Avala fleet key for {account}");
                var secret = Marshal.StringToCoTaskMemUTF8(password);

                try
                {
                    var error = IntPtr.Zero;
                    _ = secret_password_storev_sync(schema, attributes, IntPtr.Zero, label, secret, IntPtr.Zero, ref error);

                    return new Lookup(Option<string>.None, Read(error));
                }
                finally
                {
                    Wipe(secret, password.Length);
                    Marshal.FreeCoTaskMem(label);
                }
            }).Error;

        public Lookup Lookup(string account) =>
            WithSchema(account, (schema, attributes) =>
            {
                var error = IntPtr.Zero;
                var found = secret_password_lookupv_sync(schema, attributes, IntPtr.Zero, ref error);
                var problem = Read(error);

                if (found == IntPtr.Zero)
                {
                    return new Lookup(Option<string>.None, problem);
                }

                var secret = Marshal.PtrToStringUTF8(found) ?? string.Empty;
                secret_password_free(found);

                return new Lookup(secret, problem);
            });

        public Option<SecretError> Clear(string account) =>
            WithSchema(account, (schema, attributes) =>
            {
                var error = IntPtr.Zero;
                _ = secret_password_clearv_sync(schema, attributes, IntPtr.Zero, ref error);

                return new Lookup(Option<string>.None, Read(error));
            }).Error;

        private static void Wipe(IntPtr text, int length)
        {
            Marshal.Copy(new byte[Math.Max(length, 1)], 0, text, Math.Max(length, 1));
            Marshal.FreeCoTaskMem(text);
        }

        private static Option<SecretError> Read(IntPtr error)
        {
            if (error == IntPtr.Zero)
            {
                return Option<SecretError>.None;
            }

            var domain = Marshal.PtrToStringUTF8(g_quark_to_string(Marshal.ReadInt32(error, 0))) ?? string.Empty;
            var code = Marshal.ReadInt32(error, 4);
            var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? string.Empty;
            g_error_free(error);

            return new SecretError(domain, code, message);
        }

        [DllImport(Glib)]
        private static extern IntPtr g_hash_table_new(IntPtr hash, IntPtr equal);

        [DllImport(Glib)]
        private static extern int g_hash_table_insert(IntPtr table, IntPtr key, IntPtr value);

        [DllImport(Glib)]
        private static extern void g_hash_table_unref(IntPtr table);

        [DllImport(Glib)]
        private static extern void g_error_free(IntPtr error);

        [DllImport(Glib)]
        private static extern IntPtr g_quark_to_string(int quark);

        [DllImport(Library)]
        private static extern IntPtr secret_schema_newv(IntPtr name, int flags, IntPtr attributes);

        [DllImport(Library)]
        private static extern void secret_schema_unref(IntPtr schema);

        [DllImport(Library)]
        private static extern int secret_password_storev_sync(IntPtr schema, IntPtr attributes, IntPtr collection, IntPtr label, IntPtr password, IntPtr cancellable, ref IntPtr error);

        [DllImport(Library)]
        private static extern IntPtr secret_password_lookupv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, ref IntPtr error);

        [DllImport(Library)]
        private static extern int secret_password_clearv_sync(IntPtr schema, IntPtr attributes, IntPtr cancellable, ref IntPtr error);

        [DllImport(Library)]
        private static extern void secret_password_free(IntPtr password);

        private Lookup WithSchema(string account, Func<IntPtr, IntPtr, Lookup> work)
        {
            var service = Marshal.StringToCoTaskMemUTF8("service");
            var accountName = Marshal.StringToCoTaskMemUTF8("account");
            var serviceValue = Marshal.StringToCoTaskMemUTF8(PlatformKeychain.Service);
            var accountValue = Marshal.StringToCoTaskMemUTF8(account);
            var schemaName = Marshal.StringToCoTaskMemUTF8(PlatformKeychain.Service);
            var types = g_hash_table_new(stringHash, stringEqual);
            var attributes = g_hash_table_new(stringHash, stringEqual);

            try
            {
                _ = g_hash_table_insert(types, service, IntPtr.Zero);
                _ = g_hash_table_insert(types, accountName, IntPtr.Zero);
                _ = g_hash_table_insert(attributes, service, serviceValue);
                _ = g_hash_table_insert(attributes, accountName, accountValue);
                var schema = secret_schema_newv(schemaName, 0, types);

                try
                {
                    return work(schema, attributes);
                }
                finally
                {
                    secret_schema_unref(schema);
                }
            }
            finally
            {
                g_hash_table_unref(attributes);
                g_hash_table_unref(types);
                Marshal.FreeCoTaskMem(schemaName);
                Marshal.FreeCoTaskMem(accountValue);
                Marshal.FreeCoTaskMem(serviceValue);
                Marshal.FreeCoTaskMem(accountName);
                Marshal.FreeCoTaskMem(service);
            }
        }
    }
}
