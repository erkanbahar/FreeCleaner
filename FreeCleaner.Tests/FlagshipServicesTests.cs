using FreeCleaner.Services;
using Xunit;

namespace FreeCleaner.Tests;

public class FlagshipServicesTests
{
    [Fact]
    public void ComputeHealthScore_HealthyMetrics_ReturnsHighScoreAndGradeAPlus()
    {
        var metrics = new SystemMetricsSnapshot(
            OsName: "Windows 11",
            CpuArchitecture: "X64",
            DotNetVersion: ".NET 10",
            DriveName: "C:",
            DriveTotalBytes: 500_000_000_000L,
            DriveFreeBytes: 350_000_000_000L,
            DriveUsedPercent: 30.0,
            RamTotalBytes: 32_000_000_000L,
            RamAvailableBytes: 20_000_000_000L,
            RamUsedPercent: 37.5);

        var (score, badge, advice) = FlagshipTelemetryAndThemeService.ComputeHealthScore(
            metrics,
            detectedJunkBytes: 20 * 1024 * 1024,
            startupCount: 4);

        Assert.Equal(100, score);
        Assert.Contains("A+", badge);
        Assert.False(string.IsNullOrWhiteSpace(advice));
    }

    [Fact]
    public void ComputeHealthScore_HeavyLoad_DeductsAndClampsScore()
    {
        var metrics = new SystemMetricsSnapshot(
            OsName: "Windows 11",
            CpuArchitecture: "X64",
            DotNetVersion: ".NET 10",
            DriveName: "C:",
            DriveTotalBytes: 500_000_000_000L,
            DriveFreeBytes: 20_000_000_000L,
            DriveUsedPercent: 96.0,
            RamTotalBytes: 16_000_000_000L,
            RamAvailableBytes: 800_000_000L,
            RamUsedPercent: 95.0);

        var (score, badge, _) = FlagshipTelemetryAndThemeService.ComputeHealthScore(
            metrics,
            detectedJunkBytes: 6L * 1024 * 1024 * 1024,
            startupCount: 25);

        Assert.InRange(score, 10, 40);
        Assert.Contains("D", badge);
    }

    [Fact]
    public void BuildSparklinePoints_ReturnsExpectedCountAndBoundedCoordinates()
    {
        var samples = new[] { 0.0, 50.0, 100.0 };
        var points = FlagshipTelemetryAndThemeService.BuildSparklinePoints(samples, maxVal: 100.0, width: 200.0, height: 80.0);

        Assert.Equal(3, points.Count);
        Assert.Equal(0.0, points[0].X, precision: 3);
        Assert.Equal(100.0, points[1].X, precision: 3);
        Assert.Equal(200.0, points[2].X, precision: 3);
        Assert.True(points[0].Y > points[2].Y); // 0% is lower on screen (higher Y) than 100%
        Assert.Empty(FlagshipTelemetryAndThemeService.BuildSparklinePoints([], 100.0, 200.0, 80.0));
    }

    [Fact]
    public void CleanerConfigData_IncludesFlagshipDefaults()
    {
        var cfg = new CleanerConfigData();

        Assert.Equal("ObsidianNeon", cfg.SelectedTheme);
        Assert.Contains("Dashboard", cfg.FavoriteSections);
        Assert.Contains("LiveMonitor", cfg.FavoriteSections);
        Assert.Contains("ObsidianNeon", ThemeManagerService.AvailableThemes);
        Assert.Contains("FluentLight", ThemeManagerService.AvailableThemes);
    }
}
