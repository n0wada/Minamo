using System.Runtime.InteropServices;
using System.Text;

namespace Spellkit.Library.Desktop;

internal static class WindowsFileDialogs
{
    private const int MaxPath = 32_768;
    private const uint OfnExplorer = 0x0008_0000;
    private const uint OfnPathMustExist = 0x0000_0800;
    private const uint OfnFileMustExist = 0x0000_1000;
    private const uint BifReturnOnlyFileSystemDirs = 0x0001;
    private const uint BifNewDialogStyle = 0x0040;

    internal static string? OpenFile(string? title, string? filter, string? initialDirectory) =>
        SelectFile(title, filter, initialDirectory, null, save: false);

    internal static string? SaveFile(string? title, string? filter, string? defaultName) =>
        SelectFile(title, filter, null, defaultName, save: true);

    internal static string? SelectFolder(string? title, string? initialDirectory)
    {
        BrowseCallback? callback = initialDirectory is null
            ? null
            : (hwnd, message, lParam, lpData) =>
            {
                const uint BffmInitialized = 1;
                const uint BffmSetSelectionW = 0x467;
                if (message == BffmInitialized)
                {
                    SendMessage(hwnd, BffmSetSelectionW, (IntPtr)1, initialDirectory);
                }

                return IntPtr.Zero;
            };
        var info = new BrowseInfo
        {
            lpszTitle = title,
            ulFlags = BifReturnOnlyFileSystemDirs | BifNewDialogStyle,
            lpfn = callback
        };

        var item = SHBrowseForFolder(ref info);
        if (item == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var path = new StringBuilder(MaxPath);
            return SHGetPathFromIDList(item, path) ? path.ToString() : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(item);
        }
    }

    private static string? SelectFile(string? title, string? filter, string? initialDirectory, string? defaultName, bool save)
    {
        var buffer = new StringBuilder(MaxPath);
        if (defaultName is not null)
        {
            buffer.Append(defaultName);
        }

        var dialog = new OpenFileName
        {
            lStructSize = Marshal.SizeOf<OpenFileName>(),
            lpstrFilter = NormalizeFilter(filter),
            lpstrFile = buffer,
            nMaxFile = buffer.Capacity,
            lpstrInitialDir = initialDirectory,
            lpstrTitle = title,
            Flags = OfnExplorer | OfnPathMustExist | (save ? 0 : OfnFileMustExist)
        };

        var selected = save ? GetSaveFileName(ref dialog) : GetOpenFileName(ref dialog);
        return selected ? buffer.ToString() : null;
    }

    private static string NormalizeFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return "All files\0*.*\0\0";
        }

        if (filter.Contains('|'))
        {
            return filter.Replace('|', '\0') + "\0\0";
        }

        return $"Files\0{filter}\0All files\0*.*\0\0";
    }

    private delegate IntPtr BrowseCallback(IntPtr hwnd, uint message, IntPtr lParam, IntPtr lpData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        internal int lStructSize;
        internal IntPtr hwndOwner;
        internal IntPtr hInstance;
        internal string? lpstrFilter;
        internal string? lpstrCustomFilter;
        internal int nMaxCustFilter;
        internal int nFilterIndex;
        internal StringBuilder? lpstrFile;
        internal int nMaxFile;
        internal StringBuilder? lpstrFileTitle;
        internal int nMaxFileTitle;
        internal string? lpstrInitialDir;
        internal string? lpstrTitle;
        internal uint Flags;
        internal short nFileOffset;
        internal short nFileExtension;
        internal string? lpstrDefExt;
        internal IntPtr lCustData;
        internal IntPtr lpfnHook;
        internal string? lpTemplateName;
        internal IntPtr pvReserved;
        internal int dwReserved;
        internal int FlagsEx;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BrowseInfo
    {
        internal IntPtr hwndOwner;
        internal IntPtr pidlRoot;
        internal string? pszDisplayName;
        internal string? lpszTitle;
        internal uint ulFlags;
        internal BrowseCallback? lpfn;
        internal IntPtr lParam;
        internal int iImage;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileName(ref OpenFileName dialog);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSaveFileName(ref OpenFileName dialog);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolder(ref BrowseInfo browseInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder path);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, string lParam);
}
