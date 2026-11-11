using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;

namespace Tempora.Classes.Utility;

// IUISettings2 ABI: https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/winrt/windows.ui.viewmanagement.h
// Keep WinRT types out of the common target framework and export dependencies.
[SupportedOSPlatform("windows")]
public sealed class WindowsTextScale : IDisposable
{
    private nint settings;
    private bool initialized;
    private bool subscribed;
    private long token;
    private readonly ChangeHandler handler = new();

    public WindowsTextScale()
    {
        try
        {
            Initialize();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public double ScaleFactor
    {
        get
        {
            Marshal.ThrowExceptionForHR(Slot<ReadScale>(6)(settings, out double scale));
            return scale;
        }
    }

    public bool TakeChange() => handler.TakeChange();

    public void Dispose()
    {
        try
        {
            if (subscribed)
            {
                subscribed = false;
                Marshal.ThrowExceptionForHR(Slot<RemoveHandler>(8)(settings, token));
            }
        }
        finally
        {
            if (settings != 0)
            {
                Marshal.Release(settings);
                settings = 0;
            }
            if (initialized)
            {
                initialized = false;
                RoUninitialize();
            }
        }
    }

    private void Initialize()
    {
        int result = RoInitialize(0);
        // Godot's window backend initializes STA; headless hosts can already be MTA.
        if (result != unchecked((int)0x80010106))
            Marshal.ThrowExceptionForHR(result);
        initialized = result >= 0;
        nint name = 0;
        nint instance = 0;
        nint callback = 0;
        try
        {
            const string className = "Windows.UI.ViewManagement.UISettings";
            Marshal.ThrowExceptionForHR(WindowsCreateString(className, (uint)className.Length, out name));
            Marshal.ThrowExceptionForHR(RoActivateInstance(name, out instance));
            var id = new Guid("bad82401-2721-44f9-bb91-2bb228be442f");
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(instance, ref id, out settings));
            callback = Marshal.GetComInterfaceForObject(handler, typeof(ITextScaleChanged));
            Marshal.ThrowExceptionForHR(Slot<AddHandler>(7)(settings, callback, out token));
            subscribed = true;
        }
        finally
        {
            if (callback != 0) Marshal.Release(callback);
            if (instance != 0) Marshal.Release(instance);
            if (name != 0) WindowsDeleteString(name);
        }
    }

    private T Slot<T>(int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(settings), index * IntPtr.Size));

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ReadScale(nint self, out double value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AddHandler(nint self, nint handler, out long token);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int RemoveHandler(nint self, long token);

    [ComVisible(true), Guid("2dbdba9d-20da-519d-9078-09f835bc5bc7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface ITextScaleChanged
    {
        [PreserveSig] int Invoke(nint sender, nint args);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class ChangeHandler : ITextScaleChanged
    {
        private int changed;
        public int Invoke(nint sender, nint args)
        {
            Interlocked.Exchange(ref changed, 1);
            return 0;
        }
        public bool TakeChange() => Interlocked.Exchange(ref changed, 0) != 0;
    }

    [DllImport("combase.dll")] private static extern int RoInitialize(uint type);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string value, uint length, out nint result);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoActivateInstance(nint name, out nint result);
}