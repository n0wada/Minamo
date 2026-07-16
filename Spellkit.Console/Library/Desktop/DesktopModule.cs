using Spellkit.Hosting;
using Spellkit.Runtime;
using Spellkit.Runtime.Types;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Spellkit.Library.Desktop;

[SpellkitModule("desktop")]
public static class DesktopModule
{
    [SpellkitCommand("Open")]
    internal static SpkObject Open(SpellkitCommandContext host, string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target)
            {
                UseShellExecute = true
            });
            return Nil;
        }
        catch (Exception ex)
        {
            return host.ExecutionContext.IOFailed(ex.Message);
        }
    }

    [SpellkitCommand("GetText", Type = "Clipboard")]
    internal static SpkObject ClipboardGetText(SpellkitCommandContext host) =>
        WindowsOnly(host, static () => SpkString.Get(WindowsClipboard.GetText()));

    [SpellkitCommand("SetText", Type = "Clipboard")]
    internal static SpkObject ClipboardSetText(SpellkitCommandContext host, string text) =>
        WindowsOnly(host, () =>
        {
            WindowsClipboard.SetText(text);
            return Nil;
        });

    [SpellkitCommand("Clear", Type = "Clipboard")]
    internal static SpkObject ClipboardClear(SpellkitCommandContext host) =>
        WindowsOnly(host, static () =>
        {
            WindowsClipboard.Clear();
            return Nil;
        });

    [SpellkitCommand("Message", Type = "Dialog")]
    internal static SpkObject Message(SpellkitCommandContext host, string text, string? title = null) =>
        WindowsOnly(host, () =>
        {
            NativeMethods.MessageBoxW(IntPtr.Zero, text, title ?? string.Empty, 0);
            return Nil;
        });

    [SpellkitCommand("Confirm", Type = "Dialog")]
    internal static SpkObject Confirm(SpellkitCommandContext host, string text, string? title = null) =>
        WindowsOnly(host, () =>
            NativeMethods.MessageBoxW(IntPtr.Zero, text, title ?? string.Empty, 0x00000004) == 6 ? True : False);

    [SpellkitCommand("OpenFile", Type = "Dialog")]
    internal static SpkObject OpenFile(
        SpellkitCommandContext host,
        string? title = null,
        string? filter = null,
        string? initialDirectory = null) =>
        WindowsOnly(host, () => WindowsFileDialogs.OpenFile(title, filter, initialDirectory) is { } path
            ? SpkString.Get(path)
            : Nil);

    [SpellkitCommand("SaveFile", Type = "Dialog")]
    internal static SpkObject SaveFile(
        SpellkitCommandContext host,
        string? title = null,
        string? filter = null,
        string? defaultName = null) =>
        WindowsOnly(host, () => WindowsFileDialogs.SaveFile(title, filter, defaultName) is { } path
            ? SpkString.Get(path)
            : Nil);

    [SpellkitCommand("SelectFolder", Type = "Dialog")]
    internal static SpkObject SelectFolder(
        SpellkitCommandContext host,
        string? title = null,
        string? initialDirectory = null) =>
        WindowsOnly(host, () => WindowsFileDialogs.SelectFolder(title, initialDirectory) is { } path
            ? SpkString.Get(path)
            : Nil);

    [SpellkitCommand("Notify")]
    internal static SpkObject Notify(SpellkitCommandContext host, string title, string message) =>
        WindowsOnly(host, () =>
        {
            WindowsNotification.Show(title, message);
            return Nil;
        });

    private static SpkObject WindowsOnly(SpellkitCommandContext host, Func<SpkObject> action)
    {
        if (!OperatingSystem.IsWindows())
        {
            return host.ExecutionContext.IOFailed("This desktop operation is only available on Windows.");
        }

        try
        {
            return action();
        }
        catch (Exception ex)
        {
            return host.ExecutionContext.IOFailed(ex.Message);
        }
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
