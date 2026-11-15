using System.IO;
using System.Security.Cryptography;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public static class DiskAnalyzerService
{
    public static async Task<List<LargeOrDuplicateFileItem>> ScanLargeFilesAsync(
        string rootFolderPath,
        long minSizeBytes,
        IReadOnlyList<string> exclusions,
        IProgress<(string CurrentDir, int FoundCount)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<LargeOrDuplicateFileItem>();
            if (string.IsNullOrWhiteSpace(rootFolderPath) || !Directory.Exists(rootFolderPath))
                return results;

            var pending = new Stack<string>();
            pending.Push(rootFolderPath);
            int processedDirs = 0;

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string currentDir = pending.Pop();

                if (ExclusionAndSettingsService.IsPathExcluded(currentDir, exclusions))
                    continue;

                processedDirs++;
                if (processedDirs % 25 == 0)
                {
                    progress?.Report((currentDir, results.Count));
                }

                try
                {
                    var dirInfo = new DirectoryInfo(currentDir);
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    foreach (var file in dirInfo.GetFiles())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            if (file.Length >= minSizeBytes)
                            {
                                results.Add(new LargeOrDuplicateFileItem
                                {
                                    FullPath = file.FullName,
                                    FileName = file.Name,
                                    Extension = string.IsNullOrEmpty(file.Extension) ? "DOSYA" : file.Extension,
                                    SizeBytes = file.Length,
                                    LastModified = file.LastWriteTime,
                                    IsDuplicate = false,
                                    IsSelected = false
                                });
                            }
                        }
                        catch
                        {
                            // Ignore individual file errors
                        }
                    }

                    foreach (var subDir in dirInfo.GetDirectories())
                    {
                        // Skip Windows & system volume directories when scanning user root
                        if (subDir.Name.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                            subDir.Name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase) ||
                            subDir.Name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase))
                            continue;

                        pending.Push(subDir.FullName);
                    }
                }
                catch
                {
                    // Ignore inaccessible directories
                }
            }

            return results
                .OrderByDescending(x => x.SizeBytes)
                .Take(1000)
                .ToList();
        }, cancellationToken);
    }

    public static async Task<List<LargeOrDuplicateFileItem>> ScanDuplicateFilesAsync(
        string rootFolderPath,
        long minSizeBytes,
        IReadOnlyList<string> exclusions,
        IProgress<(string StatusText, int FoundDuplicates)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var allCandidateFiles = new List<FileInfo>();
            if (string.IsNullOrWhiteSpace(rootFolderPath) || !Directory.Exists(rootFolderPath))
                return [];

            var pending = new Stack<string>();
            pending.Push(rootFolderPath);

            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string currentDir = pending.Pop();

                if (ExclusionAndSettingsService.IsPathExcluded(currentDir, exclusions))
                    continue;

                try
                {
                    var dirInfo = new DirectoryInfo(currentDir);
                    if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    foreach (var file in dirInfo.GetFiles())
                    {
                        try
                        {
                            if (file.Length >= minSizeBytes)
                                allCandidateFiles.Add(file);
                        }
                        catch
                        {
                            // Ignore
                        }
                    }

                    foreach (var subDir in dirInfo.GetDirectories())
                    {
                        if (subDir.Name.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                            subDir.Name.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
                            subDir.Name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                            continue;

                        pending.Push(subDir.FullName);
                    }
                }
                catch
                {
                    // Ignore access errors
                }
            }

            // Group by exact byte length first (huge optimization before hashing)
            var sizeGroups = allCandidateFiles
                .GroupBy(f => f.Length)
                .Where(g => g.Count() > 1)
                .ToList();

            var duplicateItems = new List<LargeOrDuplicateFileItem>();

            foreach (var sizeGroup in sizeGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hashGroups = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in sizeGroup)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string? hash = ComputeFastPartialAndFullSha256(file.FullName);
                    if (string.IsNullOrEmpty(hash))
                        continue;

                    if (!hashGroups.TryGetValue(hash, out var list))
                    {
                        list = [];
                        hashGroups[hash] = list;
                    }
                    list.Add(file);
                }

                foreach (var (hash, files) in hashGroups.Where(kv => kv.Value.Count > 1))
                {
                    // Keep the first (oldest) unselected, mark subsequent duplicates as selected for easy cleanup
                    var ordered = files.OrderBy(f => f.CreationTimeUtc).ToList();
                    for (int i = 0; i < ordered.Count; i++)
                    {
                        var f = ordered[i];
                        duplicateItems.Add(new LargeOrDuplicateFileItem
                        {
                            FullPath = f.FullName,
                            FileName = f.Name,
                            Extension = f.Extension,
                            SizeBytes = f.Length,
                            LastModified = f.LastWriteTime,
                            DuplicateGroupHash = hash,
                            IsDuplicate = true,
                            IsSelected = i > 0 // First copy kept safe!
                        });
                    }

                    progress?.Report(($"Kopya bulundu: {ordered[0].Name}", duplicateItems.Count));
                }
            }

            return duplicateItems
                .OrderByDescending(x => x.SizeBytes)
                .ThenBy(x => x.DuplicateGroupHash)
                .ToList();
        }, cancellationToken);
    }

    private static string? ComputeFastPartialAndFullSha256(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sha = SHA256.Create();

            // For files larger than 32 MB, hash first 4MB + last 4MB + file size for fast responsiveness
            const int sampleSize = 4 * 1024 * 1024;
            if (stream.Length > 32L * 1024 * 1024)
            {
                byte[] buffer = new byte[sampleSize * 2];
                int readFirst = stream.Read(buffer, 0, sampleSize);
                stream.Seek(-sampleSize, SeekOrigin.End);
                int readLast = stream.Read(buffer, readFirst, sampleSize);
                byte[] hashBytes = sha.ComputeHash(buffer, 0, readFirst + readLast);
                return Convert.ToHexString(hashBytes);
            }
            else
            {
                byte[] hashBytes = sha.ComputeHash(stream);
                return Convert.ToHexString(hashBytes);
            }
        }
        catch
        {
            return null;
        }
    }
}
