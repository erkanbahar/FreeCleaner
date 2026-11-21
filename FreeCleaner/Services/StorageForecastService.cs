using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public sealed record DailyDriveSample(
    string DateKey,
    long UsedBytes,
    long TotalBytes,
    long FreeBytes);

public sealed record StorageForecastSummary(
    double AvgDailyGrowthBytes,
    int? EstimatedDaysTo90Percent,
    string HeadlineText,
    string DetailText,
    int SampleCount);

public static class StorageForecastService
{
    private const int MaxStoredDays = 90;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string DefaultTelemetryPath =>
        Path.Combine(AppStoragePathsService.GetDataDirectory(), "drive_telemetry.json");

    public static List<DailyDriveSample> LoadSamples(string? customFilePath = null)
    {
        string path = string.IsNullOrWhiteSpace(customFilePath) ? DefaultTelemetryPath : customFilePath;
        try
        {
            if (!File.Exists(path))
                return [];

            string json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<List<DailyDriveSample>>(json) ?? [];
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Depolama tahmin verisi okuma", ex);
            return [];
        }
    }

    public static List<DailyDriveSample> RecordDailySample(
        SystemMetricsSnapshot metrics,
        string? customFilePath = null,
        DateTime? nowOverride = null)
    {
        string path = string.IsNullOrWhiteSpace(customFilePath) ? DefaultTelemetryPath : customFilePath;
        DateTime localDate = (nowOverride ?? DateTime.Now).Date;
        string dateKey = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        long usedBytes = Math.Max(0, metrics.DriveTotalBytes - metrics.DriveFreeBytes);
        var currentSample = new DailyDriveSample(dateKey, usedBytes, metrics.DriveTotalBytes, metrics.DriveFreeBytes);

        List<DailyDriveSample> samples = LoadSamples(path);
        int existingIdx = samples.FindIndex(s => string.Equals(s.DateKey, dateKey, StringComparison.Ordinal));
        if (existingIdx >= 0)
            samples[existingIdx] = currentSample;
        else
            samples.Add(currentSample);

        samples = samples
            .OrderBy(s => s.DateKey, StringComparer.Ordinal)
            .TakeLast(MaxStoredDays)
            .ToList();

        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(samples, JsonOptions), Encoding.UTF8);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Depolama tahmin verisi yazma", ex);
        }

        return samples;
    }

    public static StorageForecastSummary ComputeForecast(
        IReadOnlyList<DailyDriveSample> samples,
        double criticalThresholdPercent = 90.0)
    {
        if (samples.Count == 0)
        {
            return new StorageForecastSummary(
                0,
                null,
                "⏳ Veri toplanıyor",
                "Trend tahmini için en az 2 farklı güne ait ölçüm gerekir.",
                0);
        }

        DailyDriveSample latest = samples[^1];
        if (latest.TotalBytes <= 0)
        {
            return new StorageForecastSummary(0, null, "ℹ️ Bilgi bekleniyor", "Geçerli disk kapasitesi okunamadı.", samples.Count);
        }

        double currentPct = (latest.UsedBytes * 100.0) / latest.TotalBytes;
        long thresholdBytes = (long)(latest.TotalBytes * (criticalThresholdPercent / 100.0));

        if (samples.Count < 2)
        {
            return new StorageForecastSummary(
                0,
                null,
                $"📊 %{currentPct:F0} Dolu (İlk gün)",
                $"İlk örnek alındı ({CleanerRule.FormatBytes(latest.FreeBytes)} boş). Trend yarın hesaplanacak.",
                samples.Count);
        }

        var points = new List<(double DayOffset, double UsedBytes)>(samples.Count);
        DateTime? firstDay = null;
        foreach (DailyDriveSample s in samples)
        {
            if (DateTime.TryParseExact(s.DateKey, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            {
                firstDay ??= parsed;
                points.Add(((parsed - firstDay.Value).TotalDays, s.UsedBytes));
            }
        }

        if (points.Count < 2 || Math.Abs(points[^1].DayOffset - points[0].DayOffset) < 0.01)
        {
            return new StorageForecastSummary(
                0,
                null,
                $"📊 %{currentPct:F0} Dolu",
                $"Günlük büyüme için yarınki ölçüm bekleniyor ({samples.Count} kayıt).",
                samples.Count);
        }

        double meanX = points.Average(p => p.DayOffset);
        double meanY = points.Average(p => p.UsedBytes);
        double numerator = points.Sum(p => (p.DayOffset - meanX) * (p.UsedBytes - meanY));
        double denominator = points.Sum(p => Math.Pow(p.DayOffset - meanX, 2));
        double slopeBytesPerDay = denominator > 0 ? numerator / denominator : 0;

        if (latest.UsedBytes >= thresholdBytes)
        {
            return new StorageForecastSummary(
                slopeBytesPerDay,
                0,
                $"⚠️ %{criticalThresholdPercent:F0}+ Sınırı Aşıldı",
                $"Disk %{currentPct:F1} dolu. Hemen Treemap veya Derin Alan Kurtarma çalıştırın.",
                samples.Count);
        }

        const double minNoiseSlopeBytes = 20.0 * 1024.0 * 1024.0; // < 20 MB/day = stable
        if (slopeBytesPerDay <= minNoiseSlopeBytes)
        {
            string trendNote = slopeBytesPerDay < -minNoiseSlopeBytes
                ? $"Günlük ortalama {CleanerRule.FormatBytes((long)Math.Abs(slopeBytesPerDay))} azalıyor."
                : "Disk kullanımı kararlı / artmıyor.";
            return new StorageForecastSummary(
                slopeBytesPerDay,
                null,
                "🟢 Kararlı Kullanım",
                $"{trendNote} ({samples.Count} günlük seri)",
                samples.Count);
        }

        long bytesRemainingToThreshold = Math.Max(0, thresholdBytes - latest.UsedBytes);
        int estDays = Math.Max(1, (int)Math.Ceiling(bytesRemainingToThreshold / slopeBytesPerDay));

        string dailyRateText = $"{CleanerRule.FormatBytes((long)slopeBytesPerDay)}/gün";
        string headline = estDays <= 14
            ? $"🔴 ~{estDays} gün (%{criticalThresholdPercent:F0} sınırına)"
            : $"📈 ~{estDays} gün (%{criticalThresholdPercent:F0} sınırına)";

        return new StorageForecastSummary(
            slopeBytesPerDay,
            estDays,
            headline,
            $"Ortalama +{dailyRateText} büyüme ({samples.Count} günlük analiz).",
            samples.Count);
    }
}
