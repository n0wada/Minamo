using System.Runtime.InteropServices;
using System.Text;

namespace Spellkit.Library.Desktop;

internal static class WindowsClipboard
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    internal static string GetText()
    {
        using var clipboard = ClipboardScope.Open();
        var handle = GetClipboardData(CfUnicodeText);
        if (handle == IntPtr.Zero)
        {
            return "";
        }

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to lock clipboard data.");
        }

        try
        {
            return Marshal.PtrToStringUni(pointer) ?? "";
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    internal static void SetText(string text)
    {
        using var clipboard = ClipboardScope.Open();
        EmptyClipboard();

        var bytes = Encoding.Unicode.GetBytes(text + "\0");
        var handle = GlobalAlloc(GmemMoveable, (UIntPtr)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Unable to allocate clipboard memory.");
        }

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw new InvalidOperationException("Unable to lock clipboard memory.");
        }

        try
        {
            Marshal.Copy(bytes, 0, pointer, bytes.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        if (SetClipboardData(CfUnicodeText, handle) == IntPtr.Zero)
        {
            GlobalFree(handle);
            throw new InvalidOperationException("Unable to set clipboard text.");
        }
    }

    internal static void Clear()
    {
        using var clipboard = ClipboardScope.Open();
        EmptyClipboard();
    }

    private sealed class ClipboardScope : IDisposable
    {
        private ClipboardScope() { }

        internal static ClipboardScope Open()
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                throw new InvalidOperationException("Unable to open the clipboard.");
            }

            return new ClipboardScope();
        }

        public void Dispose() => CloseClipboard();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);
}
