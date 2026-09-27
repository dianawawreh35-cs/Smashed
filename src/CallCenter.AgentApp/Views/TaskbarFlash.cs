using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CallCenter.AgentApp.Views;

/// <summary>
/// Flashes a window's taskbar button until the agent turns to it (M-A05): the
/// way a window asks for attention without taking the keyboard.
/// </summary>
internal static class TaskbarFlash
{
    private const uint FlashAll = 0x3;           // FLASHW_ALL: caption and taskbar button
    private const uint UntilForeground = 0xC;    // FLASHW_TIMERNOFG: until it comes to the front

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    /// <summary>Never throws: a button that does not flash still leaves a pop-up on top.</summary>
    public static void UntilActivated(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var info = new FlashInfo
            {
                Size = (uint)Marshal.SizeOf<FlashInfo>(),
                Window = handle,
                Flags = FlashAll | UntilForeground,
            };

            FlashWindowEx(ref info);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }
}
