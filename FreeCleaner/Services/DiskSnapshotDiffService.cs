using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public sealed record DiskFolderSnapshotEntry(
    string FullPath,
    string Name,
    long SizeBytes,
    bool IsDirectory);

public sealed record DiskFolderSnapshot(
    string RootPath,
    DateTime CapturedUtc,
    long TotalBytes,
    List<DiskFolderSnapshotEntry> Entries);

public sealed record DiskGrowthDiffItem(
    string Name,
    string FullPath,
    long PreviousBytes,
    long CurrentBytes,
    long DeltaBytes,
    bool IsNew)
{
    public string FormattedPrevious => IsNew ? "—" : CleanerRule.FormatBytes(PreviousBytes);
    public string FormattedCurrent => CleanerRule.FormatBytes(CurrentBytes);
    public string FormattedDelta => DeltaBytes >= 0
        ? $"+{CleanerRule.FormatBytes(DeltaBytes)}"
        : $"-{CleanerRule.FormatBytes(Math.Abs(DeltaBytes))}";
    public string StatusBadge => IsNew ? "🆕 Yeni" : (DeltaBytes >= 0 ? "📈 Büyüdü" : "📉 Küçüldü");
}

public static class DiskSnapshotDiffService
{
    private static string DefaultSnapshotsDir => Path.Combine(
        AppStoragePathsService.GetDataDirectory(),
        "snapshots");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string GetSnapshotFilePath(string rootPath, string? snapshotDir = null)
    {
        string folder = string.IsNullOrWhiteSpace(snapshotDir) ? DefaultSnapshotsDir : snapshotDir;
        string normalized = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        string key = Convert.ToHexString(hash)[..16];
        return Path.Combine(folder, $"snap_{key}.json");
    }

    public static DiskFolderSnapshot SaveSnapshot(
        string rootPath,
        long totalBytes,
        IEnumerable<RawTreemapEntry> entries,
        string? snapshotDir = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        string filePath = GetSnapshotFilePath(rootPath, snapshotDir);
        string? parentDir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(parentDir))
            Directory.CreateDirectory(parentDir);

        var snapshotEntries = entries
            .Where(e => !string.IsNullOrWhiteSpace(e.FullPath))
            .Select(e => new DiskFolderSnapshotEntry(e.FullPath, e.Name, e.SizeBytes, e.IsDirectory))
            .ToList();

        var snapshot = new DiskFolderSnapshot(
            RootPath: Path.GetFullPath(rootPath),
            CapturedUtc: DateTime.UtcNow,
            TotalBytes: totalBytes,
            Entries: snapshotEntries);

        string json = JsonSerializer.Serialize(snapshot, JsonOptions);
        AtomicFileStorageService.WriteAllTextAtomic(filePath, json, Encoding.UTF8, createBackup: true);

        return snapshot;
    }

    public static DiskFolderSnapshot? LoadSnapshot(string rootPath, string? snapshotDir = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return null;

        try
        {
            string filePath = GetSnapshotFilePath(rootPath, snapshotDir);
            return AtomicFileStorageService.ReadJsonWithBackupFallback<DiskFolderSnapshot>(
                filePath,
                logArea: "Disk snapshot okuma");
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Disk snapshot okuma", ex);
            return null;
        }
    }

    public static List<DiskGrowthDiffItem> ComputeGrowthDiff(
        DiskFolderSnapshot baseline,
        IEnumerable<RawTreemapEntry> currentEntries,
        long minAbsDeltaBytes = 1024)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        var baselineMap = new Dictionary<string, DiskFolderSnapshotEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (DiskFolderSnapshotEntry item in baseline.Entries)
        {
            if (!string.IsNullOrWhiteSpace(item.FullPath))
                baselineMap[item.FullPath] = item;
        }

        var diffList = new List<DiskGrowthDiffItem>();
        foreach (RawTreemapEntry current in currentEntries)
        {
            if (string.IsNullOrWhiteSpace(current.FullPath))
                continue;

            bool existed = baselineMap.TryGetValue(current.FullPath, out DiskFolderSnapshotEntry? prev);
            long prevBytes = existed && prev is not null ? prev.SizeBytes : 0;
            long deltaBytes = current.SizeBytes - prevBytes;

            if (Math.Abs(deltaBytes) >= minAbsDeltaBytes)
            {
                diffList.Add(new DiskGrowthDiffItem(
                    Name: current.Name,
                    FullPath: current.FullPath,
                    PreviousBytes: prevBytes,
                    CurrentBytes: current.SizeBytes,
                    DeltaBytes: deltaBytes,
                    IsNew: !existed));
            }
        }

        return diffList
            .OrderByDescending(x => x.DeltaBytes)
            .ToList();
    }
}
