using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Tempora.Tools.StartupSmoke;

internal static class DesktopProcess
{
    internal static int Run(ProcessStartInfo start, bool showWindow, TimeSpan timeout)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Desktop isolation requires Windows.");
        string desktopName = showWindow ? CurrentDesktopName() : $"TemporaStartup-{Guid.NewGuid():N}";
        using var desktop = showWindow ? null : CreatePrivateDesktop(desktopName);
        ProcessInformation info = StartProcess(start, desktopName);
        using var process = new SafeProcessHandle(info.Process, true);
        using var thread = new SafeWaitHandle(info.Thread, true);
        try
        {
            return WaitForExit(process, timeout);
        }
        finally
        {
            StopProcessTree(process, info.ProcessId);
        }
    }

    internal static string CurrentDesktopName()
    {
        IntPtr desktop = GetThreadDesktop(GetCurrentThreadId());
        GetUserObjectInformationW(desktop, 2, null, 0, out int bytes);
        var name = new StringBuilder(bytes / sizeof(char));
        if (!GetUserObjectInformationW(desktop, 2, name, bytes, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return name.ToString();
    }

    private static DesktopHandle CreatePrivateDesktop(string name)
    {
        var handle = CreateDesktopW(name, IntPtr.Zero, IntPtr.Zero, 0, 0x1ff, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error);
        }
        return handle;
    }

    private static ProcessInformation StartProcess(ProcessStartInfo start, string desktopName)
    {
        var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>(), Desktop = desktopName };
        string commandLine = string.Join(' ', new[] { start.FileName }.Concat(start.ArgumentList).Select(QuoteArgument));
        string environment = EnvironmentBlock(start);
        // No inherited handles: lpDesktop selects the desktop, and unrelated pipes cannot stay open.
        if (!CreateProcessW(start.FileName, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
            CreateNoWindow | CreateUnicodeEnvironment, environment, start.WorkingDirectory, ref startup, out ProcessInformation info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return info;
    }

    private static string EnvironmentBlock(ProcessStartInfo start) =>
        string.Join('\0', start.Environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value}")) + "\0\0";

    private static int WaitForExit(SafeProcessHandle process, TimeSpan timeout)
    {
        uint status = WaitForSingleObject(process, checked((uint)timeout.TotalMilliseconds));
        if (status == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (status == WaitTimeout) return -1;
        if (!GetExitCodeProcess(process, out int exitCode)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return exitCode;
    }

    private static void StopProcessTree(SafeProcessHandle handle, uint processId)
    {
        if (WaitForSingleObject(handle, 0) != WaitTimeout) return;
        using var process = Process.GetProcessById((int)processId);
        process.Kill(entireProcessTree: true);
        if (WaitForSingleObject(handle, uint.MaxValue) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static string QuoteArgument(string value)
    {
        var quoted = new StringBuilder("\"");
        int backslashes = 0;
        foreach (char character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            quoted.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
            quoted.Append(character);
            backslashes = 0;
        }
        return quoted.Append('\\', backslashes * 2).Append('"').ToString();
    }

    private const uint CreateNoWindow = 0x08000000;
    private const uint CreateUnicodeEnvironment = 0x400;
    private const uint WaitTimeout = 258;

    private sealed class DesktopHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public DesktopHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseDesktop(handle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X, Y, Width, Height, Columns, Rows, FillAttribute, Flags;
        public ushort ShowWindow, ReservedSize;
        public IntPtr ReservedData, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern DesktopHandle CreateDesktopW(string name, IntPtr device, IntPtr mode, uint flags, uint access, IntPtr security);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetThreadDesktop(uint threadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserObjectInformationW(IntPtr handle, int index, StringBuilder? information, int bytes, out int needed);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder commandLine, IntPtr processSecurity, IntPtr threadSecurity,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, string environment, string directory, ref StartupInfo startup, out ProcessInformation process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out int exitCode);
}