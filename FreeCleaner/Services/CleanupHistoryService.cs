using System.Globalization;
using System.IO;
using System.Text.Json;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

/// <summary>
/// Persists lifetime space recovery history and builds recent daily bar chart series for the Dashboard.
/// </summary>
public static class CleanupHistoryService
{
    private const int MaxStoredEntries = 500;
    private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

    private static string DefaultHistoryPath => Path.Combine(
        AppStoragePathsService.GetDataDirectory(),
        "history.json");

    public static List<CleanupHistoryEntry> LoadHistory(string? customFilePath = null)
    {
        string path = string.IsNullOrWhiteSpace(customFilePath) ? DefaultHistoryPath : customFilePath;
        try
        {
            if (!File.Exists(path))
                return [];

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<List<CleanupHistoryEntry>>(json) ?? [];
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Temizlik geçmişi okuma", ex);
            return [];
        }
    }

    public static void RecordSession(
        string moduleName,
        long freedBytes,
        int itemsProcessed,
        string? customFilePath = null)
    {
        if (freedBytes <= 0 && itemsProcessed <= 0)
            return;

        string path = string.IsNullOrWhiteSpace(customFilePath) ? DefaultHistoryPath : customFilePath;
        try
        {
            List<CleanupHistoryEntry> entries = LoadHistory(path);
            entries.Add(new CleanupHistoryEntry(
                TimestampUtc: DateTime.UtcNow,
                ModuleName: string.IsNullOrWhiteSpace(moduleName) ? "Bakım" : moduleName,
                FreedBytes: Math.Max(0, freedBytes),
                ItemsProcessed: Math.Max(0, itemsProcessed)));

            if (entries.Count > MaxStoredEntries)
                entries = entries.Skip(entries.Count - MaxStoredEntries).ToList();

            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            string json = JsonSerializer.Serialize(entries, IndentedJsonOptions);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Temizlik geçmişi kaydetme", ex);
        }
    }

    public static long GetTotalLifetimeFreedBytes(IReadOnlyList<CleanupHistoryEntry> entries)
        => entries.Where(e => e.FreedBytes > 0).Sum(e => e.FreedBytes);

    public static List<CleanupHistoryBarItem> BuildRecentDailyBars(
        IReadOnlyList<CleanupHistoryEntry> entries,
        int days = 14,
        double maxBarHeight = 56.0)
    {
        if (days <= 0)
            return [];

        DateTime todayLocal = DateTime.Now.Date;
        DateTime startDay = todayLocal.AddDays(-(days - 1));

        var dailyTotals = new long[days];
        foreach (CleanupHistoryEntry entry in entries)
        {
            if (entry.FreedBytes <= 0)
                continue;

            DateTime localDay = entry.TimestampUtc.ToLocalTime().Date;
            int offset = (localDay - startDay).Days;
            if (offset >= 0 && offset < days)
                dailyTotals[offset] += entry.FreedBytes;
        }

        long maxDayBytes = dailyTotals.Max();
        var bars = new List<CleanupHistoryBarItem>(days);

        for (int i = 0; i < days; i++)
        {
            DateTime day = startDay.AddDays(i);
            long dayBytes = dailyTotals[i];
            double barHeight = maxDayBytes > 0 && dayBytes > 0
                ? Math.Max(8.0, Math.Round(dayBytes * maxBarHeight / maxDayBytes, 1))
                : 4.0;

            string dayLabel = day.ToString("dd.MM", CultureInfo.InvariantCulture);
            string formattedSize = CleanerRule.FormatBytes(dayBytes);
            bars.Add(new CleanupHistoryBarItem(
                DayLabel: dayLabel,
                FreedBytes: dayBytes,
                BarHeight: barHeight,
                FormattedSize: formattedSize,
                ToolTipText: $"{day:dd MMM yyyy}: {formattedSize}"));
        }

        return bars;
    }
}
