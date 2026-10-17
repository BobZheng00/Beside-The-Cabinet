// Hidden test runs (shoot.ps1 -Hidden, the smoke suite, clip.ps1), loaded with Add-Type: C# 5, as Windows PowerShell
// compiles it.
//
// Backstage starts the app on a Windows desktop of its own. Nothing there ever reaches the player's screen, taskbar
// (its button, its tray icon, its notices) or focus: Godot shows its window, and asks for the focus, before any of the
// app's code runs, so keeping it off the player's desktop is the only way to keep that from them. Windows draws nothing
// on a desktop no one looks at, so the app pictures its own windows there (TestShots, --shots). The run lives in a job
// that ends with the script, so a run killed halfway leaves nothing behind.
//
// Unseen watches the player's desktop meanwhile, and says each thing a run showed them as a "seen by the player:" line,
// which fails a smoke scenario: a window of the run's processes (the one started and everything it starts) on any
// monitor, one that would have a taskbar button, or one of them becoming the window in front. The player's own
// windows, and what they do meanwhile, never count.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public static class Backstage
{
    const string Name = "InsertCoinTests";
    static IntPtr desk;
    static readonly List<IntPtr> jobs = new List<IntPtr>();

    /// <summary>
    /// Starts a command line on the test desktop, with no console, its output and errors going to files. It and
    /// everything it starts live in a job that ends with this process: a run killed halfway leaves no copy of the app
    /// running unseen.
    /// </summary>
    public static Process Start(string commandLine, string output, string errors)
    {
        if (desk == IntPtr.Zero)
            desk = CreateDesktopW(Name, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000, IntPtr.Zero);     // GENERIC_ALL; opens it if it is there
        if (desk == IntPtr.Zero)
            throw new InvalidOperationException("CreateDesktop failed: " + Marshal.GetLastWin32Error());
        var inherit = new SECURITY_ATTRIBUTES { nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)), bInheritHandle = true };
        var o = CreateFileW(output, 0x40000000, 3, ref inherit, 2, 0x80, IntPtr.Zero);              // write, shared, always new
        var e = CreateFileW(errors, 0x40000000, 3, ref inherit, 2, 0x80, IntPtr.Zero);
        try
        {
            if (o == new IntPtr(-1) || e == new IntPtr(-1))
                throw new InvalidOperationException("Can't write " + output + " or " + errors);
            var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)), lpDesktop = Name, dwFlags = 0x100, hStdOutput = o, hStdError = e };   // STARTF_USESTDHANDLES
            var job = CreateJobObjectW(IntPtr.Zero, null);
            var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            limits.BasicLimitInformation.LimitFlags = 0x2000;                                          // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            if (job == IntPtr.Zero || !SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION))))
                throw new InvalidOperationException("Can't make a job: " + Marshal.GetLastWin32Error());
            jobs.Add(job);                                  // never closed until EndAll or this process ends
            PROCESS_INFORMATION pi;
            if (!CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, true, 0x08000004, IntPtr.Zero, null, ref si, out pi))   // CREATE_NO_WINDOW, CREATE_SUSPENDED
                throw new InvalidOperationException("CreateProcess failed: " + Marshal.GetLastWin32Error());
            try
            {
                if (!AssignProcessToJobObject(job, pi.hProcess))
                {
                    TerminateProcess(pi.hProcess, 1);
                    throw new InvalidOperationException("Can't put the run in its job: " + Marshal.GetLastWin32Error());
                }
                ResumeThread(pi.hThread);                   // in the job before it starts anything
                return Process.GetProcessById(pi.dwProcessId);
            }
            finally { CloseHandle(pi.hThread); CloseHandle(pi.hProcess); }
        }
        finally { CloseHandle(o); CloseHandle(e); }
    }

    /// <summary>Ends everything started here that is still running.</summary>
    public static void EndAll()
    {
        foreach (var job in jobs)
            CloseHandle(job);
        jobs.Clear();
    }

    /// <summary>The windows showing on the test desktop, the one on top first.</summary>
    public static IntPtr[] Windows()
    {
        var all = new List<IntPtr>();
        EnumDesktopWindows(desk, (h, p) => { if (IsWindowVisible(h)) all.Add(h); return true; }, IntPtr.Zero);
        return all.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)] struct SECURITY_ATTRIBUTES { public int nLength; public IntPtr lpSecurityDescriptor; public bool bInheritHandle; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb; public string lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }
    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] struct IO_COUNTERS { public ulong Reads, Writes, Others, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation; public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateJobObjectW(IntPtr sa, string name);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr process, uint code);
    delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateDesktopW(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr sa);
    [DllImport("user32.dll")] static extern bool EnumDesktopWindows(IntPtr desk, EnumProc f, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateProcessW(string app, string commandLine, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateFileW(string name, uint access, uint share, ref SECURITY_ATTRIBUTES sa, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
}

public sealed class Unseen : IDisposable
{
    readonly HashSet<uint> ours = new HashSet<uint>(), others = new HashSet<uint>();     // only the watching thread uses these
    readonly HashSet<IntPtr> before = new HashSet<IntPtr>();
    readonly List<string> seen = new List<string>();
    readonly HashSet<string> said = new HashSet<string>();
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly Thread thread;
    volatile bool stop;

    /// <summary>Starts watching the windows of <paramref name="root"/> and every process it starts.</summary>
    public Unseen(int root)
    {
        ours.Add((uint)root);
        EnumWindows((h, p) => { before.Add(h); return true; }, IntPtr.Zero);
        thread = new Thread(Watch) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    /// <summary>Stops watching; what was seen, one line each (none: nothing was).</summary>
    public string[] Stop()
    {
        stop = true;
        thread.Join();
        lock (seen) return seen.ToArray();
    }

    public void Dispose() { Stop(); }

    void Watch()
    {
        // every 20 ms: light (a few hundred windows looked at, the process list read only for a window not seen before),
        // and short enough to catch a window that shows for a moment
        while (!stop)
        {
            var front = GetForegroundWindow();
            if (Counts(front))
                Saw(front, "in front (it took the focus)");
            EnumWindows((h, p) =>
            {
                if (!Counts(h) || !IsWindowVisible(h) || Cloaked(h))
                    return true;
                RECT r;
                GetWindowRect(h, out r);
                if (r.R > r.L && r.B > r.T && MonitorFromRect(ref r, 0) != IntPtr.Zero)
                    Saw(h, "on screen");
                long ex = GetWindowLongPtr(h, -20).ToInt64();
                if ((ex & 0x40000) != 0 || (GetWindow(h, 4) == IntPtr.Zero && (ex & 0x80) == 0))     // WS_EX_APPWINDOW; unowned and no WS_EX_TOOLWINDOW
                    Saw(h, "on the taskbar");
                return true;
            }, IntPtr.Zero);
            Thread.Sleep(20);
        }
    }

    bool Counts(IntPtr h)
    {
        if (h == IntPtr.Zero || before.Contains(h))
            return false;
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        if (ours.Contains(pid))
            return true;
        if (others.Contains(pid))
            return false;
        Grow();                                     // a new window of a process not met yet: did the run start it?
        if (ours.Contains(pid))
            return true;
        others.Add(pid);
        return false;
    }

    void Saw(IntPtr h, string what)
    {
        var line = string.Format("seen by the player: {0} at {1:0.00} s, {2}", what, clock.Elapsed.TotalSeconds, Describe(h));
        lock (seen)
            if (said.Add(h.ToInt64() + what))
                seen.Add(line);
    }

    static string Describe(IntPtr h)
    {
        if (h == IntPtr.Zero)
            return "no window";
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        var title = new StringBuilder(256);
        GetWindowText(h, title, 256);
        RECT r;
        GetWindowRect(h, out r);
        string name;
        try { name = Process.GetProcessById((int)pid).ProcessName; } catch (ArgumentException) { name = "pid " + pid; }
        return string.Format("{0} '{1}' at {2},{3} {4}x{5}", name, title, r.L, r.T, r.R - r.L, r.B - r.T);
    }

    static bool Cloaked(IntPtr h)
    {
        int cloaked;
        return DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0;      // DWMWA_CLOAKED
    }

    // every process whose parent is one of ours is ours too (the console wrapper starts the real Godot)
    void Grow()
    {
        var snap = CreateToolhelp32Snapshot(2, 0);
        if (snap == new IntPtr(-1))
            return;
        try
        {
            var e = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
            var found = new List<KeyValuePair<uint, uint>>();
            for (var more = Process32First(snap, ref e); more; more = Process32Next(snap, ref e))
                found.Add(new KeyValuePair<uint, uint>(e.th32ProcessID, e.th32ParentProcessID));
            lock (ours)
                for (var grew = true; grew; )
                {
                    grew = false;
                    foreach (var pair in found)
                        if (!ours.Contains(pair.Key) && ours.Contains(pair.Value))
                            grew = ours.Add(pair.Key);
                }
        }
        finally { CloseHandle(snap); }
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct PROCESSENTRY32
    {
        public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }
    delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr p);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref RECT r, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out int value, int size);
    [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] static extern bool Process32First(IntPtr snap, ref PROCESSENTRY32 e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] static extern bool Process32Next(IntPtr snap, ref PROCESSENTRY32 e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
}
