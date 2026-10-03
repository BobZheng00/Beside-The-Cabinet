using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace InsertCoin.Game.App;

/// <summary>
/// Ties a helper process (her voice server, a training run) to the cabinet: they all join one Windows job, whose only
/// handle closes when the cabinet exits or crashes, and Windows then ends them (and whatever they started). Windows'
/// background throttling is switched off for them, since nobody looks at their windows (they have none).
/// </summary>
[SupportedOSPlatform("windows")]
public static class CabinetJob
{
    private static IntPtr _job;

    public static void Tie(Process process, string what)
    {
        try
        {
            if (_job == IntPtr.Zero)
            {
                _job = CreateJobObject(IntPtr.Zero, null);
                var limits = new ExtendedLimits { Basic = new BasicLimits { LimitFlags = KillOnJobClose } };
                SetInformationJobObject(_job, ExtendedLimitInformation, ref limits, Marshal.SizeOf<ExtendedLimits>());
            }
            AssignProcessToJobObject(_job, process.Handle);
            var throttling = new PowerThrottling { Version = 1, ControlMask = 1, StateMask = 0 };    // execution speed: not throttled
            SetProcessInformation(process.Handle, ProcessPowerThrottling, ref throttling, Marshal.SizeOf<PowerThrottling>());
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Godot.GD.PrintErr($"Voice: could not tie {what} to the cabinet: {e.Message}");
        }
    }

    private const int ExtendedLimitInformation = 9, ProcessPowerThrottling = 4;
    private const uint KillOnJobClose = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimits Basic;
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottling
    {
        public uint Version, ControlMask, StateMask;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits info, int length);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottling info, int length);
}
