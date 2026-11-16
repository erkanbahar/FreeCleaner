using FreeCleaner.Models;
using FreeCleaner.Services;
using System.Globalization;
using Xunit;

namespace FreeCleaner.Tests;

public class CleanerEngineTests
{
    [Fact]
    public void DefaultRules_ClassifyHighRiskOperations()
    {
        var rules = CleanerEngine.GetDefaultRules();

        Assert.Equal(RiskLevel.High, rules.Single(rule => rule.Id == "sys_recycle_bin").Risk);
        Assert.Equal(RiskLevel.High, rules.Single(rule => rule.Id == "brw_chrome_cookies").Risk);
        Assert.Equal(RiskLevel.High, rules.Single(rule => rule.Id == "brw_edge_cookies").Risk);
    }

    [Fact]
    public void FormatBytes_FormatsCommonUnits()
    {
        Assert.Equal("0 B", CleanerRule.FormatBytes(0));
        string separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        Assert.Equal($"1{separator}00 KB", CleanerRule.FormatBytes(1024));
        Assert.Equal($"1{separator}00 MB", CleanerRule.FormatBytes(1024 * 1024));
    }

    [Fact]
    public async Task AnalyzeAndClean_TracksFileAndFreedBytes()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"FreeCleanerTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string filePath = Path.Combine(tempDirectory, "cache.tmp");
        await File.WriteAllTextAsync(filePath, "temporary data");
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddMinutes(-2));

        try
        {
            var rule = new CleanerRule
            {
                Id = "test_temp",
                Name = "Test Temp",
                Category = "Test",
                Description = "Test",
                IconGlyph = string.Empty,
                Group = RuleGroup.System,
                Risk = RiskLevel.Low,
                Targets = [new ScanTarget(tempDirectory)]
            };
            var engine = new CleanerEngine();

            var results = await engine.AnalyzeAsync([rule]);
            var item = Assert.Single(results);
            Assert.Equal(filePath, item.FullPath);
            Assert.Equal(RiskLevel.Low, item.Risk);

            var summary = await engine.CleanAsync(results, [rule], moveToRecycleBinInsteadOfPermanentDelete: false);

            Assert.Equal(1, summary.DeletedFilesCount);
            Assert.Equal(item.SizeBytes, summary.FreedBytes);
            Assert.False(File.Exists(filePath));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
