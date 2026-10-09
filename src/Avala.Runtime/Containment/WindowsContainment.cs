using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Containment;

internal sealed class WindowsContainment : IContainment
{
    public ValueTask<IContainer> CreateAsync(ProcessTreeId tree, CancellationToken cancellationToken)
    {
        try
        {
            return ValueTask.FromResult<IContainer>(WindowsJob.Create());
        }
        catch (Win32Exception)
        {
            return ValueTask.FromResult<IContainer>(new RootsContainer());
        }
    }
}

internal sealed class RootsContainer : IContainer
{
    private System.Collections.Immutable.ImmutableHashSet<int> roots = [];

    public ProcessStartInfo Prepare(ProcessStartInfo info) => info;

    public void Adopt(Process process) => System.Collections.Immutable.ImmutableInterlocked.Update(ref roots, known => known.Add(process.Id));

    public ValueTask<IReadOnlyList<int>> MemberIdsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<int>>([.. Volatile.Read(ref roots).Where(Alive)]);

    public void Dispose()
    {
    }

    private static bool Alive(int root)
    {
        try
        {
            using var process = Process.GetProcessById(root);

            return !process.HasExited;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }
}

internal static class WindowsProcesses
{
    private const int BasicInformation = 0;

    public static Option<int> ParentOf(int member)
    {
        try
        {
            using var process = Process.GetProcessById(member);

            return NtQueryInformationProcess(process.Handle, BasicInformation, out var information, Marshal.SizeOf<ProcessInformation>(), out _) == 0
                ? (int)information.InheritedFromUniqueProcessId.ToInt64()
                : Option<int>.None;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return Option<int>.None;
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, out ProcessInformation information, int length, out int returned);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }
}

internal sealed class WindowsJob : IContainer
{
    private const int ExtendedLimitInformation = 9;
    private const int BasicProcessIdList = 3;
    private const uint KillOnJobClose = 0x2000;
    private const int MoreData = 234;

    private readonly IntPtr job;
    private int disposed;

    private WindowsJob(IntPtr job) => this.job = job;

    public static WindowsJob Create()
    {
        var job = CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);

        if (job == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };

        if (!SetInformationJobObject(job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        {
            var error = Marshal.GetLastWin32Error();
            _ = CloseHandle(job);

            throw new Win32Exception(error);
        }

        return new WindowsJob(job);
    }

    public ProcessStartInfo Prepare(ProcessStartInfo info) => info;

    public void Adopt(Process process) => _ = AssignProcessToJobObject(job, process.Handle);

    public ValueTask<IReadOnlyList<int>> MemberIdsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(MemberIds(64));

    private IReadOnlyList<int> MemberIds(int capacity) =>
        Query(capacity).Match(ids => ids, () => MemberIds(capacity * 2));

    private Option<IReadOnlyList<int>> Query(int capacity)
    {
        var size = 8 + (IntPtr.Size * capacity);
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            if (QueryInformationJobObject(job, BasicProcessIdList, buffer, (uint)size, out _))
            {
                var count = Marshal.ReadInt32(buffer, 4);

                return Option<IReadOnlyList<int>>.Some(
                    [.. Enumerable.Range(0, count).Select(index => (int)Marshal.ReadIntPtr(buffer, 8 + (IntPtr.Size * index)))]);
            }

            return Marshal.GetLastWin32Error() == MoreData ? Option<IReadOnlyList<int>>.None : Option<IReadOnlyList<int>>.Some([]);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            _ = CloseHandle(job);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, IntPtr name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass, ref ExtendedLimits information, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(IntPtr job, int informationClass, IntPtr information, uint length, out uint returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
