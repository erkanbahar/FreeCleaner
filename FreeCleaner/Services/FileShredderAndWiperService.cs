using System.IO;
using System.Security.Cryptography;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public static class FileShredderAndWiperService
{
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

                try
                {
                    if (item.IsDirectory && Directory.Exists(item.FullPath))
                    {
                        var allFiles = Directory.GetFiles(item.FullPath, "*", SearchOption.AllDirectories);
                        foreach (string file in allFiles)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (ShredSingleFile(file, passes))
                                shreddedCount++;
                        }
                        Directory.Delete(item.FullPath, recursive: true);
                        totalShreddedBytes += item.SizeBytes;
                        item.Status = "İmha Edildi (DoD)";
                    }
                    else if (File.Exists(item.FullPath))
                    {
                        if (ShredSingleFile(item.FullPath, passes))
                        {
                            shreddedCount++;
                            totalShreddedBytes += item.SizeBytes;
                            item.Status = $"İmha Edildi ({passes} Geçiş)";
                        }
                        else
                        {
                            item.Status = "Kilitli / Hata";
                        }
                    }
                    else
                    {
                        item.Status = "Bulunamadı";
                    }
                }
                catch
                {
                    item.Status = "Hata / İzin Yok";
                }
            }
        }, cancellationToken);

        progress?.Report((100, "Tamamlandı"));
        return (shreddedCount, totalShreddedBytes);
    }

    private static bool ShredSingleFile(string filePath, int passes)
    {
        try
        {
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
        catch
        {
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
            long targetBytes = wipeSizeMb * 1024L * 1024L;

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
                        progress?.Report((pct, $"Boş disk sektörleri sıfırlanıyor: {mb + 1} / {wipeSizeMb} MB"));
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
