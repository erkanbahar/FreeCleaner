using System.IO;
using System.Text;
using System.Text.Json;

namespace FreeCleaner.Services;

/// <summary>
/// Provides crash-safe atomic file persistence (temp file flush + backup swap)
/// and automatic .bak recovery for JSON state files.
/// </summary>
public static class AtomicFileStorageService
{
    public static void WriteAllTextAtomic(
        string targetPath,
        string content,
        Encoding? encoding = null,
        bool createBackup = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(content);

        string fullTarget = Path.GetFullPath(targetPath);
        string? parentDir = Path.GetDirectoryName(fullTarget);
        if (!string.IsNullOrEmpty(parentDir))
            Directory.CreateDirectory(parentDir);

        Encoding enc = encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        string tempPath = $"{fullTarget}.{Guid.NewGuid():N}.tmp";

        try
        {
            byte[] payload = enc.GetBytes(content);
            using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                fs.Write(payload);
                fs.Flush(flushToDisk: true);
            }

            if (createBackup && File.Exists(fullTarget))
            {
                try
                {
                    File.Copy(fullTarget, fullTarget + ".bak", overwrite: true);
                }
                catch (Exception bakEx)
                {
                    ErrorLogService.Log("AtomicStorage:Backup", bakEx);
                }
            }

            File.Move(tempPath, fullTarget, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Ignore temp cleanup failure.
            }
        }
    }

    public static T? ReadJsonWithBackupFallback<T>(
        string targetPath,
        JsonSerializerOptions? options = null,
        string? logArea = null)
    {
        return ReadJsonWithBackupFallback<T>(targetPath, out _, options, logArea);
    }

    public static T? ReadJsonWithBackupFallback<T>(
        string targetPath,
        out string rawJsonUsed,
        JsonSerializerOptions? options = null,
        string? logArea = null)
    {
        rawJsonUsed = string.Empty;
        if (string.IsNullOrWhiteSpace(targetPath))
            return default;

        string fullTarget = Path.GetFullPath(targetPath);
        string backupPath = fullTarget + ".bak";
        string area = string.IsNullOrWhiteSpace(logArea) ? "AtomicStorage:Read" : logArea;

        if (File.Exists(fullTarget))
        {
            try
            {
                string json = File.ReadAllText(fullTarget, Encoding.UTF8);
                T? primary = JsonSerializer.Deserialize<T>(json, options);
                if (primary is not null)
                {
                    rawJsonUsed = json;
                    return primary;
                }
            }
            catch (Exception ex)
            {
                ErrorLogService.Log(area, ex);
            }
        }

        if (File.Exists(backupPath))
        {
            try
            {
                string bakJson = File.ReadAllText(backupPath, Encoding.UTF8);
                T? recovered = JsonSerializer.Deserialize<T>(bakJson, options);
                if (recovered is not null)
                {
                    ErrorLogService.Log(area, $"Recovered state file from backup: {backupPath}");
                    try
                    {
                        WriteAllTextAtomic(fullTarget, bakJson, Encoding.UTF8, createBackup: false);
                    }
                    catch (Exception restoreEx)
                    {
                        ErrorLogService.Log($"{area}:Restore", restoreEx);
                    }

                    rawJsonUsed = bakJson;
                    return recovered;
                }
            }
            catch (Exception bakEx)
            {
                ErrorLogService.Log($"{area}:BackupRead", bakEx);
            }
        }

        return default;
    }
}
