using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FreeCleaner.Models;
using Microsoft.Win32.SafeHandles;

namespace FreeCleaner.Services;

public sealed record HardlinkDeduplicationResult(
    int LinkedCount,
    int SkippedCount,
    long BytesReclaimed,
    List<string> Warnings);

public static partial class NtfsHardlinkService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public NativeFileTime CreationTime;
        public NativeFileTime LastAccessTime;
        public NativeFileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLinkW(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    public static bool IsNtfsSameVolume(string pathA, string pathB)
    {
        try
        {
            string? rootA = Path.GetPathRoot(Path.GetFullPath(pathA));
            string? rootB = Path.GetPathRoot(Path.GetFullPath(pathB));
            if (string.IsNullOrWhiteSpace(rootA) ||
                string.IsNullOrWhiteSpace(rootB) ||
                !string.Equals(rootA, rootB, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var drive = new DriveInfo(rootA);
            return drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool AreSameNtfsFile(string pathA, string pathB)
    {
        if (!TryGetFileId(pathA, out uint volA, out ulong idxA) ||
            !TryGetFileId(pathB, out uint volB, out ulong idxB))
        {
            return false;
        }

        return volA == volB && idxA == idxB;
    }

    private static bool TryGetFileId(string path, out uint volumeSerial, out ulong fileIndex)
    {
        volumeSerial = 0;
        fileIndex = 0;
        try
        {
            using var fs = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            if (!GetFileInformationByHandle(fs.SafeFileHandle, out var info))
                return false;

            volumeSerial = info.VolumeSerialNumber;
            fileIndex = ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<HardlinkDeduplicationResult> DeduplicateWithHardlinksAsync(
        IEnumerable<LargeOrDuplicateFileItem> items,
        CancellationToken ct = default)
    {
        int linkedCount = 0;
        int skippedCount = 0;
        long bytesReclaimed = 0;
        var warnings = new List<string>();

        var groups = items
            .Where(item => item.IsDuplicate &&
                           !string.IsNullOrWhiteSpace(item.DuplicateGroupHash) &&
                           File.Exists(item.FullPath))
            .GroupBy(item => item.DuplicateGroupHash!, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            var groupItems = group.ToList();
            if (groupItems.Count < 2)
                continue;

            // Keep an unselected canonical file if available; otherwise keep the first item untouched.
            var master = groupItems.FirstOrDefault(x => !x.IsSelected) ?? groupItems[0];
            var selectedDuplicates = groupItems
                .Where(x => x.IsSelected && !string.Equals(x.FullPath, master.FullPath, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (selectedDuplicates.Count == 0)
                continue;

            var masterInfo = new FileInfo(master.FullPath);
            if (!masterInfo.Exists)
                continue;

            string masterHash;
            try
            {
                masterHash = await ComputeSha256Async(master.FullPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                warnings.Add($"{master.FileName}: Ana dosya okunamadı ({ex.Message}).");
                skippedCount += selectedDuplicates.Count;
                continue;
            }

            foreach (var dup in selectedDuplicates)
            {
                ct.ThrowIfCancellationRequested();

                var dupInfo = new FileInfo(dup.FullPath);
                if (!dupInfo.Exists)
                {
                    skippedCount++;
                    continue;
                }

                if (!IsNtfsSameVolume(master.FullPath, dup.FullPath))
                {
                    skippedCount++;
                    warnings.Add($"{dup.FileName}: Farklı sürücü veya NTFS olmayan birim — hardlink kurulamadı.");
                    continue;
                }

                if (AreSameNtfsFile(master.FullPath, dup.FullPath))
                {
                    skippedCount++;
                    continue;
                }

                if (dupInfo.Length != masterInfo.Length)
                {
                    skippedCount++;
                    warnings.Add($"{dup.FileName}: Boyut uyuşmazlığı nedeniyle atlandı.");
                    continue;
                }

                string dupHash;
                try
                {
                    dupHash = await ComputeSha256Async(dup.FullPath, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    skippedCount++;
                    warnings.Add($"{dup.FileName}: SHA-256 doğrulaması başarısız ({ex.Message}).");
                    continue;
                }

                if (!string.Equals(masterHash, dupHash, StringComparison.OrdinalIgnoreCase))
                {
                    skippedCount++;
                    warnings.Add($"{dup.FileName}: İçerik SHA-256 uyuşmadığı için güvenlik gereği atlandı.");
                    continue;
                }

                if (TryReplaceWithHardlinkAtomic(dup.FullPath, master.FullPath, out string? error))
                {
                    linkedCount++;
                    bytesReclaimed += dupInfo.Length;
                    dup.IsSelected = false;
                }
                else
                {
                    skippedCount++;
                    warnings.Add($"{dup.FileName}: {error ?? "Hardlink oluşturulamadı."}");
                }
            }
        }

        return new HardlinkDeduplicationResult(linkedCount, skippedCount, bytesReclaimed, warnings);
    }

    private static bool TryReplaceWithHardlinkAtomic(string duplicatePath, string masterPath, out string? error)
    {
        error = null;
        string? dir = Path.GetDirectoryName(duplicatePath);
        if (string.IsNullOrEmpty(dir))
        {
            error = "Hedef klasör yolu çözülemedi.";
            return false;
        }

        FileAttributes originalAttrs = FileAttributes.Normal;
        string tempBackup = Path.Combine(dir, $".fc_hl_bak_{Guid.NewGuid():N}.tmp");

        try
        {
            originalAttrs = File.GetAttributes(duplicatePath);
            if ((originalAttrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(duplicatePath, originalAttrs & ~FileAttributes.ReadOnly);

            File.Move(duplicatePath, tempBackup);

            if (!CreateHardLinkW(duplicatePath, masterPath, IntPtr.Zero))
            {
                int win32Err = Marshal.GetLastWin32Error();
                File.Move(tempBackup, duplicatePath, overwrite: true);
                if ((originalAttrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(duplicatePath, originalAttrs);

                error = $"Win32 CreateHardLinkW hatası ({win32Err}).";
                return false;
            }

            try
            {
                if (File.Exists(tempBackup))
                {
                    File.SetAttributes(tempBackup, FileAttributes.Normal);
                    File.Delete(tempBackup);
                }
            }
            catch
            {
                // Link is already established; best-effort cleanup of temp backup if locked.
            }

            return true;
        }
        catch (Exception ex)
        {
            try
            {
                if (!File.Exists(duplicatePath) && File.Exists(tempBackup))
                    File.Move(tempBackup, duplicatePath);
            }
            catch
            {
                // Ignore secondary rollback failure.
            }

            error = ex.Message;
            return false;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 131072,
            useAsync: true);

        byte[] hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }
}
