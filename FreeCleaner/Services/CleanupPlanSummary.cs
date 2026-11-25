using FreeCleaner.Models;

namespace FreeCleaner.Services;

/// <summary>
/// Text for the cleaning confirmation dialog: what will be removed, grouped by cleaning rule (largest first),
/// and how much goes to the Recycle Bin versus is deleted permanently.
/// </summary>
public static class CleanupPlanSummary
{
    public static string Describe(
        IEnumerable<ScannedFileItem> selectedItems,
        bool useRecycleBin,
        string? language = null,
        int maxRules = 6)
    {
        ArgumentNullException.ThrowIfNull(selectedItems);
        var items = selectedItems.ToList();
        if (items.Count == 0)
            return string.Empty;

        string T(string tr, string en) => LocalizationService.GetText(tr, en, language);
        string FileCount(int n) => T($"{n:N0} dosya", $"{n:N0} files");

        var groups = items
            .GroupBy(i => i.RuleName)
            .Select(g => (Name: g.Key, Bytes: g.Sum(i => i.SizeBytes), Count: g.Count()))
            .OrderByDescending(g => g.Bytes)
            .ThenBy(g => g.Name, StringComparer.CurrentCulture)
            .ToList();

        var lines = new List<string>();
        foreach (var g in groups.Take(maxRules))
            lines.Add($"• {g.Name}: {CleanerRule.FormatBytes(g.Bytes)} ({FileCount(g.Count)})");

        var rest = groups.Skip(maxRules).ToList();
        if (rest.Count > 0)
        {
            long restBytes = rest.Sum(g => g.Bytes);
            int restCount = rest.Sum(g => g.Count);
            lines.Add(T(
                $"• +{rest.Count} kural daha: {CleanerRule.FormatBytes(restBytes)} ({FileCount(restCount)})",
                $"• +{rest.Count} more rules: {CleanerRule.FormatBytes(restBytes)} ({FileCount(restCount)})"));
        }

        // The Recycle Bin's own contents are always emptied permanently; everything else follows the setting.
        long recycleBinContents = items.Where(i => i.IsRecycleBinItem).Sum(i => i.SizeBytes);
        long others = items.Where(i => !i.IsRecycleBinItem).Sum(i => i.SizeBytes);
        long toRecycleBin = useRecycleBin ? others : 0;
        long permanent = useRecycleBin ? recycleBinContents : others + recycleBinContents;

        lines.Add(string.Empty);
        if (toRecycleBin > 0)
        {
            lines.Add(T(
                $"Geri Dönüşüm Kutusu'na taşınacak (geri alınabilir): {CleanerRule.FormatBytes(toRecycleBin)}",
                $"Moved to the Recycle Bin (can be undone): {CleanerRule.FormatBytes(toRecycleBin)}"));
        }
        if (permanent > 0)
        {
            lines.Add(T(
                $"Kalıcı olarak silinecek: {CleanerRule.FormatBytes(permanent)}" + (recycleBinContents > 0 ? " (Geri Dönüşüm Kutusu içeriği dahil)" : string.Empty),
                $"Deleted permanently: {CleanerRule.FormatBytes(permanent)}" + (recycleBinContents > 0 ? " (includes Recycle Bin contents)" : string.Empty)));
        }

        return string.Join("\n", lines);
    }

    /// <summary>One line for the status bar after a cleanup, e.g. "12 in use • 3 need administrator".</summary>
    public static string DescribeSkipped(CleanSummaryResult summary, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        string T(string tr, string en) => LocalizationService.GetText(tr, en, language);

        var parts = new List<string>();
        if (summary.InUseCount > 0)
            parts.Add(T($"{summary.InUseCount:N0} dosya kullanımda", $"{summary.InUseCount:N0} files in use"));
        if (summary.AccessDeniedCount > 0)
            parts.Add(T($"{summary.AccessDeniedCount:N0} dosya yönetici yetkisi istiyor", $"{summary.AccessDeniedCount:N0} files need administrator rights"));
        int other = summary.SkippedFilesCount - summary.InUseCount - summary.AccessDeniedCount;
        if (other > 0)
            parts.Add(T($"{other:N0} dosya atlandı", $"{other:N0} files skipped"));
        return string.Join(" • ", parts);
    }

    /// <summary>Explains what the analysis deliberately left out; empty when nothing was skipped.</summary>
    public static string DescribeAnalysisNotes(AnalysisNotes notes, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        string T(string tr, string en) => LocalizationService.GetText(tr, en, language);

        var parts = new List<string>();
        if (notes.SkippedRecentTempFiles > 0)
        {
            parts.Add(T(
                $"Son 24 saatte yazılmış {notes.SkippedRecentTempFiles:N0} geçici dosya ({CleanerRule.FormatBytes(notes.SkippedRecentTempBytes)}) korundu",
                $"{notes.SkippedRecentTempFiles:N0} temp files written in the last 24 hours ({CleanerRule.FormatBytes(notes.SkippedRecentTempBytes)}) were kept"));
        }
        int adminPaths = notes.SkippedAdminOnlyPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if (adminPaths > 0)
        {
            parts.Add(T(
                $"{adminPaths} sistem klasörü yönetici yetkisi gerektirdiği için taranmadı",
                $"{adminPaths} system folders were not scanned because they require administrator rights"));
        }
        return string.Join(" • ", parts);
    }
}
