using System.IO;
using System.Text;
using System.Text.Json;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public sealed record OrganizePlanItem(
    string FileName,
    string SourcePath,
    string TargetCategory,
    string DestinationPath,
    long SizeBytes)
{
    public string FormattedSize => CleanerRule.FormatBytes(SizeBytes);
}

public sealed record OrganizeUndoEntry(string OriginalPath, string MovedPath);

public sealed record OrganizeUndoJournal(
    DateTime ExecutedUtc,
    string RootFolder,
    List<OrganizeUndoEntry> Moves);

public static class SmartFolderOrganizerService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string DefaultUndoJournalPath =>
        Path.Combine(AppStoragePathsService.GetDataDirectory(), "organizer_last_undo.json");

    private static readonly HashSet<string> SkippedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk", ".url", ".ini", ".tmp", ".crdownload", ".part", ".opdownload", ".download"
    };

    public static string ClassifyExtension(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext))
            return "Diğer";

        return ext.Trim().ToLowerInvariant() switch
        {
            ".exe" or ".msi" or ".msix" or ".msixbundle" or ".appx" or ".bat" or ".cmd" or ".iso" or ".img"
                => "Kurulumlar",
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".md" or ".csv" or ".rtf"
                => "Belgeler",
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or ".svg" or ".ico" or ".heic" or ".tif" or ".tiff"
                => "Görseller",
            ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".mp3" or ".wav" or ".flac" or ".m4a"
                => "Medya",
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".xz" or ".bz2"
                => "Arşivler",
            ".cs" or ".py" or ".js" or ".ts" or ".json" or ".xml" or ".yaml" or ".yml" or ".sql" or ".ps1" or ".sh" or ".html" or ".css"
                => "Kod",
            _ => "Diğer"
        };
    }

    public static List<OrganizePlanItem> BuildOrganizePlan(string rootFolder)
    {
        if (string.IsNullOrWhiteSpace(rootFolder) || !Directory.Exists(rootFolder))
            return [];

        string fullRoot = Path.GetFullPath(rootFolder);
        var plan = new List<OrganizePlanItem>();
        var plannedDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (FileInfo file in new DirectoryInfo(fullRoot).EnumerateFiles("*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if ((file.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) != 0)
                    continue;

                if (SkippedExtensions.Contains(file.Extension))
                    continue;

                string category = ClassifyExtension(file.Extension);
                string targetDir = Path.Combine(fullRoot, category);
                string destPath = GetCollisionSafeDestination(targetDir, file.Name, plannedDestinations);
                plannedDestinations.Add(destPath);

                plan.Add(new OrganizePlanItem(
                    file.Name,
                    file.FullName,
                    category,
                    destPath,
                    file.Length));
            }
            catch
            {
                // Ignore files disappearing or locked during enumeration.
            }
        }

        return plan
            .OrderBy(p => p.TargetCategory, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GetCollisionSafeDestination(
        string targetDir,
        string fileName,
        HashSet<string> reservedPaths)
    {
        string baseName = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        string candidate = Path.Combine(targetDir, fileName);

        int suffix = 1;
        while (File.Exists(candidate) || reservedPaths.Contains(candidate))
        {
            candidate = Path.Combine(targetDir, $"{baseName} ({suffix}){ext}");
            suffix++;
        }

        return candidate;
    }

    public static (int MovedCount, int FailedCount, List<string> Notes) ExecutePlan(
        IEnumerable<OrganizePlanItem> plan,
        string rootFolder,
        string? journalPathOverride = null)
    {
        var items = plan.ToList();
        if (items.Count == 0)
            return (0, 0, ["Taşınacak dosya bulunamadı."]);

        var movedEntries = new List<OrganizeUndoEntry>();
        int failed = 0;
        var notes = new List<string>();

        foreach (OrganizePlanItem item in items)
        {
            try
            {
                if (!File.Exists(item.SourcePath))
                {
                    failed++;
                    continue;
                }

                string? destDir = Path.GetDirectoryName(item.DestinationPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                string finalDest = item.DestinationPath;
                if (File.Exists(finalDest))
                    finalDest = GetCollisionSafeDestination(destDir!, Path.GetFileName(item.DestinationPath), []);

                File.Move(item.SourcePath, finalDest);
                movedEntries.Add(new OrganizeUndoEntry(item.SourcePath, finalDest));
            }
            catch (Exception ex)
            {
                failed++;
                notes.Add($"{item.FileName}: {ex.Message}");
            }
        }

        if (movedEntries.Count > 0)
        {
            string journalPath = string.IsNullOrWhiteSpace(journalPathOverride)
                ? DefaultUndoJournalPath
                : journalPathOverride;

            string? jDir = Path.GetDirectoryName(journalPath);
            if (!string.IsNullOrEmpty(jDir))
                Directory.CreateDirectory(jDir);

            var journal = new OrganizeUndoJournal(DateTime.UtcNow, Path.GetFullPath(rootFolder), movedEntries);
            File.WriteAllText(journalPath, JsonSerializer.Serialize(journal, JsonOptions), Encoding.UTF8);
        }

        return (movedEntries.Count, failed, notes);
    }

    public static bool HasUndoJournal(string? journalPathOverride = null)
    {
        string journalPath = string.IsNullOrWhiteSpace(journalPathOverride)
            ? DefaultUndoJournalPath
            : journalPathOverride;
        return File.Exists(journalPath);
    }

    public static (int RestoredCount, int FailedCount, string Message) UndoLastOrganize(string? journalPathOverride = null)
    {
        string journalPath = string.IsNullOrWhiteSpace(journalPathOverride)
            ? DefaultUndoJournalPath
            : journalPathOverride;

        if (!File.Exists(journalPath))
            return (0, 0, "Geri alınacak klasör düzenleme kaydı bulunamadı.");

        try
        {
            string json = File.ReadAllText(journalPath, Encoding.UTF8);
            var journal = JsonSerializer.Deserialize<OrganizeUndoJournal>(json);
            if (journal is null || journal.Moves.Count == 0)
                return (0, 0, "Geri alma kaydı boş.");

            int restored = 0;
            int failed = 0;
            var touchedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = journal.Moves.Count - 1; i >= 0; i--)
            {
                OrganizeUndoEntry entry = journal.Moves[i];
                try
                {
                    if (!File.Exists(entry.MovedPath))
                    {
                        failed++;
                        continue;
                    }

                    string? movedDir = Path.GetDirectoryName(entry.MovedPath);
                    if (!string.IsNullOrEmpty(movedDir))
                        touchedDirs.Add(movedDir);

                    string? origDir = Path.GetDirectoryName(entry.OriginalPath);
                    if (!string.IsNullOrEmpty(origDir))
                        Directory.CreateDirectory(origDir);

                    if (!File.Exists(entry.OriginalPath))
                    {
                        File.Move(entry.MovedPath, entry.OriginalPath);
                        restored++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch
                {
                    failed++;
                }
            }

            foreach (string dir in touchedDirs)
            {
                try
                {
                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch { }
            }

            if (failed == 0)
            {
                try { File.Delete(journalPath); } catch { }
            }

            return (restored, failed, $"↩️ {restored} dosya orijinal konumuna geri alındı (hata: {failed}).");
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Klasör düzenleme geri alma", ex);
            return (0, 0, $"Geri alma hatası: {ex.Message}");
        }
    }
}
