using System.IO;
using System.Runtime.InteropServices;

namespace FreeCleaner.Services;

public static class RecycleBinHelper
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [Flags]
    private enum RecycleFlags : uint
    {
        SHERB_NOCONFIRMATION = 0x00000001,
        SHERB_NOPROGRESSUI = 0x00000002,
        SHERB_NOSOUND = 0x00000004
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_SILENT = 0x0004;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, RecycleFlags dwFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    public static (long SizeBytes, int ItemCount) QueryRecycleBin()
    {
        try
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            int hr = SHQueryRecycleBin(null, ref info);
            if (hr == 0)
            {
                return (Math.Max(0, info.i64Size), (int)Math.Max(0, info.i64NumItems));
            }
        }
        catch
        {
            // Ignore P/Invoke failure
        }
        return (0, 0);
    }

    public static bool EmptyRecycleBin()
    {
        try
        {
            int hr = SHEmptyRecycleBin(
                IntPtr.Zero,
                null,
                RecycleFlags.SHERB_NOCONFIRMATION | RecycleFlags.SHERB_NOPROGRESSUI | RecycleFlags.SHERB_NOSOUND);
            return hr == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool MoveFileToRecycleBin(string filePath)
        => MovePathToRecycleBin(filePath);

    public static bool MovePathToRecycleBin(string targetPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                return false;

            if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
                return false;

            string normalized = targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fs = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = normalized + '\0' + '\0',
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT
            };
            int ret = SHFileOperation(ref fs);
            return ret == 0 && !fs.fAnyOperationsAborted;
        }
        catch
        {
            return false;
        }
    }
}
