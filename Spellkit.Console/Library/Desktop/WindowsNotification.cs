using System.Runtime.InteropServices;

namespace Spellkit.Library.Desktop;

internal static class WindowsNotification
{
    private const uint NimAdd = 0x00000000;
    private const uint NimModify = 0x00000001;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint NifInfo = 0x00000010;
    private const uint NiifInfo = 0x00000001;
    private static readonly IntPtr IconInformation = (IntPtr)32516;

    internal static void Show(string title, string message)
    {
        var data = new NotifyIconData
        {
            cbSize = Marshal.SizeOf<NotifyIconData>(),
            hWnd = GetConsoleWindow(),
            uID = 1,
            uFlags = NifMessage | NifIcon | NifTip,
            hIcon = LoadIcon(IntPtr.Zero, IconInformation),
            szTip = "Spellkit"
        };

        if (!ShellNotifyIcon(NimAdd, ref data))
        {
            throw new InvalidOperationException("Unable to register the desktop notification.");
        }

        try
        {
            data.uFlags = NifInfo;
            data.szInfoTitle = title;
            data.szInfo = message;
            data.dwInfoFlags = NiifInfo;
            if (!ShellNotifyIcon(NimModify, ref data))
            {
                throw new InvalidOperationException("Unable to show the desktop notification.");
            }
        }
        finally
        {
            ShellNotifyIcon(NimDelete, ref data);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        internal int cbSize;
        internal IntPtr hWnd;
        internal uint uID;
        internal uint uFlags;
        internal uint uCallbackMessage;
        internal IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string szTip;
        internal uint dwState;
        internal uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string szInfo;
        internal uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string szInfoTitle;
        internal uint dwInfoFlags;
        internal Guid guidItem;
        internal IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr iconName);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();
}
