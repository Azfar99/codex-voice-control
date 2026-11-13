using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace CodexVoice {
sealed class BackgroundCodex : IDisposable {
    // These rights allow window discovery and creation on our desktop. They do
    // not include the right to switch the user's desktop.
    const uint ReadObjects = 0x0001;
    const uint CreateWindow = 0x0002;
    const uint Enumerate = 0x0040;
    const uint WriteObjects = 0x0080;
    const uint DesktopAccess = ReadObjects | CreateWindow | Enumerate | WriteObjects;
    const uint CreateUnicodeEnvironment = 0x00000400;
    const int UseShowWindow = 0x0001;
    const int DisableStartupCursor = 0x0080;
    const int ShowWithoutActivation = 4;
    const int CloseWindowMessage = 0x0010;
    public const string NewChatLink = "codex://threads/new";

    readonly string name;
    readonly string profile;
    IntPtr desktop;
    volatile Process started;
    bool closeRequested;

    public BackgroundCodex(string profileDirectory) {
        profile = Path.GetFullPath(profileDirectory);
        using (var sha = SHA256.Create()) {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(profile.ToUpperInvariant()));
            name = "CodexVoiceControl-" + BitConverter.ToString(hash, 0, 12).Replace("-", "");
        }
    }

    public bool OpenExisting() {
        if (desktop != IntPtr.Zero) return true;
        desktop = OpenDesktopW(name, 0, false, DesktopAccess);
        if (desktop == IntPtr.Zero) {
            int error = Marshal.GetLastWin32Error();
            if (error != 2 && error != 6)
                throw new Win32Exception(error, "Could not open the background desktop.");
        }
        return desktop != IntPtr.Zero;
    }

    public void EnsureStarted() {
        if (OpenExisting()) {
            // Reuse an existing renderer or one that is still starting.
            if ((started != null && !started.HasExited) || Windows().Count != 0) {
                if (closeRequested)
                    throw new InvalidOperationException("Background Codex is still closing. Try again in a moment.");
                return;
            }
            Dispose();
        }
        closeRequested = false;
        var originalDesktop = GetThreadDesktop(GetCurrentThreadId());
        desktop = CreateDesktopW(name, null, IntPtr.Zero, 0, DesktopAccess, IntPtr.Zero);
        if (desktop == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the background desktop.");
        try {
            // CreateDesktop can associate the caller with the new desktop.
            // Return the worker to its original desktop before UI Automation.
            if (originalDesktop != IntPtr.Zero && GetThreadDesktop(GetCurrentThreadId()) != originalDesktop && !SetThreadDesktop(originalDesktop))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not restore the voice-control worker desktop.");
            started = LaunchBackground();
        } catch {
            Dispose();
            throw;
        }
    }

    Process LaunchBackground() {
        string executable = AppDiscovery.FindExecutable();
        if (String.IsNullOrEmpty(executable))
            throw new InvalidOperationException("Install the OpenAI desktop app and sign in to Codex once.");

        Directory.CreateDirectory(profile);
        // Keep the background renderer separate from the regular profile.
        var environment = Environment.GetEnvironmentVariables();
        environment["CODEX_ELECTRON_USER_DATA_PATH"] = profile;
        environment["CODEX_ELECTRON_START_IN_BACKGROUND"] = "1";
        string entries = String.Join("\0", environment.Keys.Cast<string>()
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Select(key => key + "=" + environment[key]));
        var environmentBlock = Marshal.StringToHGlobalUni(entries + "\0");
        var startup = new StartupInfo {
            Size = Marshal.SizeOf(typeof(StartupInfo)),
            Desktop = name,
            Flags = UseShowWindow | DisableStartupCursor,
            Show = ShowWithoutActivation
        };
        var command = new StringBuilder("\"" + executable + "\" --user-data-dir=\"" + profile + "\"");
        try {
            ProcessInfo child;
            if (!CreateProcessW(executable, command, IntPtr.Zero, IntPtr.Zero, false,
                CreateUnicodeEnvironment, environmentBlock, Path.GetDirectoryName(executable), ref startup, out child))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the installed Codex app.");
            try { return Process.GetProcessById((int)child.Id); }
            finally { CloseHandle(child.Thread); CloseHandle(child.Process); }
        } finally { Marshal.FreeHGlobal(environmentBlock); }
    }

    public bool StartedProcessExited { get { return started != null && started.HasExited; } }
    public int StartedProcessId { get { var child = started; return child == null ? 0 : child.Id; } }

    public List<IntPtr> Windows() {
        if (!OpenExisting()) return new List<IntPtr>();
        return EnumerateWindows(desktop, delegate(IntPtr window) {
            if (!Native.IsWindowVisible(window) || GetWindowTextLengthW(window) == 0) return false;
            uint pid;
            Native.GetWindowThreadProcessId(window, out pid);
            return (started == null || pid == started.Id) && CodexBridge.IsCodexProcess(pid);
        });
    }

    public bool CloseWindows() {
        var windows = Windows();
        foreach (var window in windows) {
            if (!PostMessageW(window, CloseWindowMessage, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not close the background Codex window.");
            closeRequested = true;
        }
        return windows.Count != 0;
    }

    public void Dispose() {
        if (started != null) { started.Dispose(); started = null; }
        if (desktop != IntPtr.Zero) { CloseDesktop(desktop); desktop = IntPtr.Zero; }
    }

    // Smoke-test cleanup can only terminate a process this object created. It
    // is used with a fresh scratch profile, never a user's running app.
    internal void FinishSmokeTest() {
        try {
            if (started == null || started.HasExited) return;
            try {
                foreach (var window in Windows()) PostMessageW(window, CloseWindowMessage, IntPtr.Zero, IntPtr.Zero);
            } catch (Win32Exception) {
                // Cleanup still owns the isolated scratch process.
            }
            if (!started.WaitForExit(2000)) { started.Kill(); started.WaitForExit(2500); }
        } finally { Dispose(); }
    }

    internal static bool HasProcessOnInputDesktop(int processId) {
        IntPtr input = OpenInputDesktop(0, false, ReadObjects);
        if (input == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not inspect the input desktop.");
        try {
            return EnumerateWindows(input, delegate(IntPtr window) {
                uint pid;
                Native.GetWindowThreadProcessId(window, out pid);
                return pid == processId;
            }).Count != 0;
        } finally { CloseDesktop(input); }
    }

    internal static List<IntPtr> EnumerateWindows(IntPtr desktop, Predicate<IntPtr> include) {
        var windows = new List<IntPtr>();
        // Filter after enumeration so exceptions cannot cross the callback.
        SetLastError(0);
        if (!EnumDesktopWindows(desktop, delegate(IntPtr window, IntPtr data) {
            windows.Add(window);
            return true;
        }, IntPtr.Zero)) {
            int error = Marshal.GetLastWin32Error();
            // User32 can return false without setting an error for an empty desktop.
            if (error != 0)
                throw new Win32Exception(error, "Could not enumerate windows on the desktop (Windows error " + error + ").");
        }
        return windows.FindAll(include);
    }

    internal static List<IntPtr> CurrentDesktopWindows(Predicate<IntPtr> include) {
        var current = GetThreadDesktop(GetCurrentThreadId());
        if (current == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not identify the current desktop.");
        return EnumerateWindows(current, include);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StartupInfo {
        public int Size;
        public string Reserved, Desktop, Title;
        public int X, Y, Width, Height, CharsX, CharsY, Fill, Flags;
        public short Show, ReservedLength;
        public IntPtr ReservedBytes, Input, Output, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ProcessInfo { public IntPtr Process, Thread; public uint Id, ThreadId; }

    delegate bool WindowCallback(IntPtr window, IntPtr data);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktopW(string name, string device, IntPtr mode, uint flags, uint access, IntPtr security);
    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr OpenDesktopW(string name, uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")]
    static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool EnumDesktopWindows(IntPtr desktop, WindowCallback callback, IntPtr data);
    [DllImport("user32.dll", ExactSpelling = true)]
    static extern int GetWindowTextLengthW(IntPtr window);
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    static extern bool PostMessageW(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcessW(string executable, StringBuilder command, IntPtr processSecurity,
        IntPtr threadSecurity, bool inherit, uint flags, IntPtr environment, string workingDirectory,
        ref StartupInfo startup, out ProcessInfo info);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")]
    static extern void SetLastError(uint error);
    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();
}
}
