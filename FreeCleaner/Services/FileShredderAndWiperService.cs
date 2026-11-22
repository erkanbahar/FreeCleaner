using System.IO;
using System.Security.Cryptography;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public static class FileShredderAndWiperService
{
    public static bool IsProtectedSystemPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return true;

        string normalized;
        try
        {
            normalized = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(normalized))
            return true;

        string? root = Path.GetPathRoot(normalized)?
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!string.IsNullOrEmpty(root) &&
            string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrEmpty(userProfile))
        {
            if (string.Equals(normalized, userProfile, StringComparison.OrdinalIgnoreCase))
                return true;

            string? usersRoot = Path.GetDirectoryName(userProfile)?
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrEmpty(usersRoot) &&
                string.Equals(normalized, usersRoot, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        Environment.SpecialFolder[] systemSubtrees =
        [
            Environment.SpecialFolder.Windows,
            Environment.SpecialFolder.System,
            Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.ProgramFiles,
            Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonApplicationData
        ];

        foreach (Environment.SpecialFolder folder in systemSubtrees)
        {
            string sysPath = Environment.GetFolderPath(folder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(sysPath))
                continue;

            if (string.Equals(normalized, sysPath, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(sysPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        string appBase = AppContext.BaseDirectory
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(appBase) &&
            (string.Equals(normalized, appBase, StringComparison.OrdinalIgnoreCase) ||
             appBase.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        string? procPath = Environment.ProcessPath?
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(procPath) &&
            string.Equals(normalized, procPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static bool IsReparsePoint(string path)
    {
        try
        {
            FileAttributes attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasReparsePointAncestor(string entryPath, string boundaryRoot)
    {
        try
        {
            string boundaryFull = Path.GetFullPath(boundaryRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            DirectoryInfo? current = Directory.GetParent(entryPath);

            while (current is not null)
            {
                string currentFull = current.FullName
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.Equals(currentFull, boundaryFull, StringComparison.OrdinalIgnoreCase))
                    break;

                if (IsReparsePoint(current.FullName))
                    return true;

                current = current.Parent;
            }
        }
        catch
        {
            return true;
        }

        return false;
    }

    public static async Task<(int ShreddedCount, long TotalBytes)> ShredItemsAsync(
        IEnumerable<ShredTargetItem> items,
        int passes,
        IProgress<(int Percent, string CurrentItem)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var list = items.ToList();
        if (list.Count == 0) return (0, 0);

        int shreddedCount = 0;
        long totalShreddedBytes = 0;

        await Task.Run(() =>
        {
            for (int i = 0; i < list.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = list[i];
                int pct = (int)((i / (double)list.Count) * 100);
                progress?.Report((pct, item.Name));

                if (IsProtectedSystemPath(item.FullPath))
                {
                    ErrorLogService.Log("FileShredder", $"Blocked protected system path: {item.FullPath}");
                    item.Status = LocalizationService.GetText(
                        "Korunan Sistem Yolu (Engellendi)",
                        "Protected System Path (Blocked)");
                    continue;
                }

                try
                {
                    if (item.IsDirectory && Directory.Exists(item.FullPath))
                    {
                        if (IsReparsePoint(item.FullPath))
                        {
                            // Remove only the directory junction/symlink without touching target contents.
                            Directory.Delete(item.FullPath, recursive: false);
                            item.Status = LocalizationService.GetText(
                                "Bağlantı Kaldırıldı (Hedef Korundu)",
                                "Link Removed (Target Preserved)");
                            continue;
                        }

                        var enumOpts = new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = true,
                            AttributesToSkip = FileAttributes.ReparsePoint
                        };

                        foreach (string file in Directory.EnumerateFiles(item.FullPath, "*", enumOpts))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (IsReparsePoint(file) || HasReparsePointAncestor(file, item.FullPath))
                                continue;

                            if (ShredSingleFile(file, passes))
                                shreddedCount++;
                        }

                        DeleteDirectoryTreeWithoutFollowingReparsePoints(item.FullPath);
                        totalShreddedBytes += item.SizeBytes;
                        item.Status = LocalizationService.GetText(
                            "İmha Edildi (DoD)",
                            "Shredded (DoD)");
                    }
                    else if (File.Exists(item.FullPath))
                    {
                        if (IsReparsePoint(item.FullPath))
                        {
                            File.Delete(item.FullPath);
                            item.Status = LocalizationService.GetText(
                                "Bağlantı Kaldırıldı (Hedef Korundu)",
                                "Link Removed (Target Preserved)");
                            continue;
                        }

                        if (ShredSingleFile(item.FullPath, passes))
                        {
                            shreddedCount++;
                            totalShreddedBytes += item.SizeBytes;
                            item.Status = LocalizationService.GetText(
                                $"İmha Edildi ({passes} Geçiş)",
                                $"Shredded ({passes} Passes)");
                        }
                        else
                        {
                            item.Status = LocalizationService.GetText(
                                "Kilitli / Hata",
                                "Locked / Error");
                        }
                    }
                    else
                    {
                        item.Status = LocalizationService.GetText("Bulunamadı", "Not Found");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ErrorLogService.Log("FileShredder", ex);
                    item.Status = LocalizationService.GetText("Hata / İzin Yok", "Error / Access Denied");
                }
            }
        }, cancellationToken);

        progress?.Report((100, LocalizationService.GetText("Tamamlandı", "Completed")));
        return (shreddedCount, totalShreddedBytes);
    }

    private static void DeleteDirectoryTreeWithoutFollowingReparsePoints(string dirPath)
    {
        if (!Directory.Exists(dirPath))
            return;

        if (IsReparsePoint(dirPath))
        {
            Directory.Delete(dirPath, recursive: false);
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(dirPath))
        {
            if (IsReparsePoint(entry))
            {
                if (Directory.Exists(entry))
                    Directory.Delete(entry, recursive: false);
                else if (File.Exists(entry))
                    File.Delete(entry);
                continue;
            }

            if (Directory.Exists(entry))
            {
                DeleteDirectoryTreeWithoutFollowingReparsePoints(entry);
            }
            else if (File.Exists(entry))
            {
                FileAttributes attrs = File.GetAttributes(entry);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(entry, FileAttributes.Normal);
                File.Delete(entry);
            }
        }

        Directory.Delete(dirPath, recursive: false);
    }

    private static bool ShredSingleFile(string filePath, int passes)
    {
        try
        {
            if (IsReparsePoint(filePath))
            {
                File.Delete(filePath);
                return true;
            }

            var attrs = File.GetAttributes(filePath);
            if ((attrs & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }

            long length = new FileInfo(filePath).Length;
            byte[] buffer = new byte[64 * 1024];

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                for (int p = 0; p < passes; p++)
                {
                    stream.Position = 0;
                    long remaining = length;

                    while (remaining > 0)
                    {
                        int toWrite = (int)Math.Min(buffer.Length, remaining);
                        if (p == 0)
                        {
                            Array.Clear(buffer, 0, toWrite); // Pass 1: 0x00
                        }
                        else if (p == 1 && passes >= 3)
                        {
                            Array.Fill(buffer, (byte)0xFF, 0, toWrite); // Pass 2: 0xFF
                        }
                        else
                        {
                            RandomNumberGenerator.Fill(buffer.AsSpan(0, toWrite)); // Random cryptographic bytes
                        }

                        stream.Write(buffer, 0, toWrite);
                        remaining -= toWrite;
                    }
                    stream.Flush(flushToDisk: true);
                }

                // Truncate file to 0 bytes before deleting
                stream.SetLength(0);
            }

            // Rename file to random name to obfuscate MFT metadata entry before deletion
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                string obfuscatedPath = Path.Combine(dir, Guid.NewGuid().ToString("N")[..12] + ".tmp");
                File.Move(filePath, obfuscatedPath);
                File.Delete(obfuscatedPath);
            }
            else
            {
                File.Delete(filePath);
            }

            return true;
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("FileShredder:SingleFile", ex);
            return false;
        }
    }

    public static async Task<long> WipeDriveFreeSpaceSampleAsync(
        string driveRoot,
        int wipeSizeMb,
        IProgress<(int Percent, string Status)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            string tempWipeFile = Path.Combine(Path.GetTempPath(), $"FreeCleaner_FreeSpaceWipe_{Guid.NewGuid():N}.bin");
            long writtenBytes = 0;

            try
            {
                byte[] buffer = new byte[1024 * 1024]; // 1 MB chunks
                using (var fs = new FileStream(tempWipeFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    for (int mb = 0; mb < wipeSizeMb; mb++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        RandomNumberGenerator.Fill(buffer);
                        fs.Write(buffer, 0, buffer.Length);
                        writtenBytes += buffer.Length;

                        int pct = (int)(((mb + 1) / (double)wipeSizeMb) * 100);
                        progress?.Report((pct, LocalizationService.GetText(
                            $"Boş disk sektörleri sıfırlanıyor: {mb + 1} / {wipeSizeMb} MB",
                            $"Overwriting free disk space: {mb + 1} / {wipeSizeMb} MB")));
                    }
                    fs.Flush(flushToDisk: true);
                }
            }
            finally
            {
                if (File.Exists(tempWipeFile))
                {
                    try { File.Delete(tempWipeFile); } catch { }
                }
            }

            return writtenBytes;
        }, cancellationToken);
    }
}
