using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using FreeCleaner.Services;
using Xunit;
using Drawing = System.Drawing;
using DrawingImaging = System.Drawing.Imaging;

namespace FreeCleaner.Tests;

public class NextGenUpgradesTests
{
    [Fact]
    public void ComputeTreemapLayout_ValidEntries_FillsBoundsWithoutOverflow()
    {
        var entries = new List<RawTreemapEntry>
        {
            new("Videos", @"C:\Data\Videos", 600_000_000, IsDirectory: true),
            new("Games", @"C:\Data\Games", 300_000_000, IsDirectory: true),
            new("backup.zip", @"C:\Data\backup.zip", 100_000_000, IsDirectory: false)
        };

        const double viewportW = 800.0;
        const double viewportH = 400.0;

        var nodes = VisualDiskAndMediaService.ComputeTreemapLayout(entries, viewportW, viewportH);

        Assert.Equal(3, nodes.Count);
        foreach (var node in nodes)
        {
            Assert.True(node.Width > 0);
            Assert.True(node.Height > 0);
            Assert.InRange(node.X, 0.0, viewportW);
            Assert.InRange(node.Y, 0.0, viewportH);
            Assert.True(node.X + node.Width <= viewportW + 0.01);
            Assert.True(node.Y + node.Height <= viewportH + 0.01);
            Assert.StartsWith("#", node.FillHex);
        }

        double totalPercent = nodes.Sum(n => n.SharePercent);
        Assert.InRange(totalPercent, 99.9, 100.1);

        Assert.Empty(VisualDiskAndMediaService.ComputeTreemapLayout([], viewportW, viewportH));
    }

    [Fact]
    public async Task AnalyzeTreemapFolderAsync_ComputesNestedSizesReportsProgressAndCaches()
    {
        VisualDiskAndMediaService.ClearTreemapCache();
        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerTreemapTest_{Guid.NewGuid():N}");
        string subA = Path.Combine(tempRoot, "FolderA");
        string subB = Path.Combine(tempRoot, "FolderB", "Deep");
        Directory.CreateDirectory(subA);
        Directory.CreateDirectory(subB);

        try
        {
            byte[] dataA = new byte[4096];
            byte[] dataB = new byte[8192];
            byte[] dataRoot = new byte[1024];

            await File.WriteAllBytesAsync(Path.Combine(subA, "a.bin"), dataA);
            await File.WriteAllBytesAsync(Path.Combine(subB, "b.bin"), dataB);
            await File.WriteAllBytesAsync(Path.Combine(tempRoot, "root.bin"), dataRoot);

            var reportedSnapshots = new List<TreemapScanProgress>();
            IProgress<TreemapScanProgress> progress = new Progress<TreemapScanProgress>(p =>
            {
                lock (reportedSnapshots)
                    reportedSnapshots.Add(p);
            });

            TreemapAnalysisResult result = await VisualDiskAndMediaService.AnalyzeTreemapFolderAsync(
                tempRoot,
                canvasWidth: 800,
                canvasHeight: 400,
                progress: progress,
                forceRefresh: true);

            Assert.Equal(4096 + 8192 + 1024, result.TotalBytes);
            Assert.Equal(3, result.ScannedFileCount);
            Assert.Equal(3, result.Nodes.Count);
            Assert.False(result.UsedCache);

            // Second scan with forceRefresh=false should hit the warm subtree cache.
            TreemapAnalysisResult cachedSub = await VisualDiskAndMediaService.AnalyzeTreemapFolderAsync(
                tempRoot,
                canvasWidth: 800,
                canvasHeight: 400,
                progress: null,
                forceRefresh: false);

            Assert.Equal(result.TotalBytes, cachedSub.TotalBytes);
            Assert.True(cachedSub.UsedCache);

            // Writing an extra file and scanning with forceRefresh=true bypasses cache cleanly.
            await File.WriteAllBytesAsync(Path.Combine(subA, "a2.bin"), new byte[2048]);
            TreemapAnalysisResult refreshed = await VisualDiskAndMediaService.AnalyzeTreemapFolderAsync(
                tempRoot,
                canvasWidth: 800,
                canvasHeight: 400,
                progress: null,
                forceRefresh: true);

            Assert.Equal(result.TotalBytes + 2048, refreshed.TotalBytes);
            Assert.Equal(4, refreshed.ScannedFileCount);
            Assert.False(refreshed.UsedCache);
        }
        finally
        {
            VisualDiskAndMediaService.ClearTreemapCache();
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ExifScrubber_DetectsGpsAndExif_ThenScrubsCleanlyWithBackup()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"FreeCleanerExifTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string photoPath = Path.Combine(tempDir, "sample_gps.jpg");
            CreateSyntheticJpegWithExifAndGps(photoPath, width: 32, height: 24);

            var scanned = await VisualDiskAndMediaService.ScanPhotoMetadataAsync(tempDir, includeSubfolders: false);
            var target = Assert.Single(scanned);

            Assert.True(target.HasGpsData);
            Assert.True(target.TagCount >= 2);
            Assert.Contains("Canon", target.CameraInfo);

            var (success, _, removedTags, _) = await VisualDiskAndMediaService.ScrubPhotoMetadataAsync(
                target,
                createBackup: true,
                optimizeJpegQuality: false);

            Assert.True(success);
            Assert.True(removedTags >= 2);

            string backupPath = Path.Combine(tempDir, "FreeCleaner_PhotoBackup", "sample_gps.jpg");
            Assert.True(File.Exists(backupPath));

            // Re-scan only top folder (exclude FreeCleaner_PhotoBackup subfolder) and confirm EXIF/GPS is gone.
            var postScan = await VisualDiskAndMediaService.ScanPhotoMetadataAsync(tempDir, includeSubfolders: false);
            var cleaned = Assert.Single(postScan);
            Assert.False(cleaned.HasGpsData);
            Assert.Equal(0, cleaned.TagCount);

            // Ensure pixel dimensions survived cleanly.
            using var verifiedBmp = new Drawing.Bitmap(photoPath);
            Assert.Equal(32, verifiedBmp.Width);
            Assert.Equal(24, verifiedBmp.Height);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("4.0.0", "v4.1.0", true)]
    [InlineData("4.0.0", "v4.0.1", true)]
    [InlineData("4.0.0", "v4.2.0-beta.1", true)]
    [InlineData("4.0.0", "v4.0.0", false)]
    [InlineData("4.0.0", "v3.9.9", false)]
    [InlineData("4.0.0", "invalid-tag", false)]
    public void AppUpdateService_IsNewerVersion_ComparesSemverTagsAccurately(string current, string latestTag, bool expected)
    {
        Assert.Equal(expected, AppUpdateService.IsNewerVersion(current, latestTag));
    }

    [Fact]
    public void LocalizationService_HasCompleteKeyParityBetweenTurkishAndEnglish()
    {
        var trKeys = LocalizationService.GetAllKeys("tr-TR").ToHashSet(StringComparer.Ordinal);
        var enKeys = LocalizationService.GetAllKeys("en-US").ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(trKeys);
        Assert.Equal(trKeys.Count, enKeys.Count);
        Assert.Empty(trKeys.Except(enKeys));
        Assert.Empty(enKeys.Except(trKeys));

        foreach (string key in trKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(LocalizationService.Get(key, "tr-TR")), $"Missing tr-TR value for {key}");
            Assert.False(string.IsNullOrWhiteSpace(LocalizationService.Get(key, "en-US")), $"Missing en-US value for {key}");
        }

        Assert.NotEqual(
            LocalizationService.GetSectionTitle("DiskTreemap", "tr-TR"),
            LocalizationService.GetSectionTitle("DiskTreemap", "en-US"));

        Assert.Equal("tr-TR", new CleanerConfigData().SelectedLanguage);
    }

    [Fact]
    public void LazyViewRegistry_InstantiatesOnceOnFirstDemandAndCaches()
    {
        int calls = 0;
        var registry = new LazyViewRegistry();
        registry.Register("DiskTreemap", () =>
        {
            calls++;
            return new object();
        });

        Assert.Equal(0, registry.LoadedCount);
        Assert.True(registry.HasView("DiskTreemap"));
        Assert.False(registry.HasView("UnknownSection"));

        object? first = registry.GetOrCreate("DiskTreemap");
        object? second = registry.GetOrCreate("DiskTreemap");

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, calls);
        Assert.Equal(1, registry.LoadedCount);
        Assert.Null(registry.GetOrCreate("UnknownSection"));
    }

    [Fact]
    public void BrowserOnDeviceAiModels_RuleAndAiHunter_DetectOptGuideAndWeightsBin()
    {
        var rules = CleanerEngine.GetDefaultRules();
        var aiBrowserRule = Assert.Single(rules, r => r.Id == "brw_ondevice_ai_models");

        Assert.Equal(FreeCleaner.Models.RuleGroup.Browsers, aiBrowserRule.Group);
        Assert.Equal(FreeCleaner.Models.RiskLevel.Medium, aiBrowserRule.Risk);
        Assert.Contains(aiBrowserRule.Targets, t => t.BasePath.Contains("OptGuideOnDeviceModel", StringComparison.OrdinalIgnoreCase));

        Assert.True(EighthGenAiAndSecuritySuiteService.IsStandaloneAiWeightFile(@"C:\temp\weights.bin", 60L * 1024 * 1024));
        Assert.False(EighthGenAiAndSecuritySuiteService.IsStandaloneAiWeightFile(@"C:\temp\weights.bin", 1024));
        Assert.True(EighthGenAiAndSecuritySuiteService.IsStandaloneAiWeightFile(@"C:\models\llama.gguf", 20L * 1024 * 1024));

        var tweaks = EighthGenAiAndSecuritySuiteService.GetWindowsTweaksState();
        Assert.Contains(tweaks, t => t.TweakId == "DISABLE_BROWSER_ONDEVICE_AI");
    }

    [Fact]
    public async Task VhdxCompactAndClaudeVm_ScriptAndModelProperties_WorkAccurately()
    {
        var vhdxItem = new FreeCleaner.Models.GlobalPackageCacheItem
        {
            Ecosystem = "Claude Desktop VM (VHDX)",
            CacheName = "Sanal Disk: rootfs.vhdx",
            FolderPath = @"C:\Users\Test\AppData\Local\Claude-Data\abc\vm_bundles\claudevm.bundle\rootfs.vhdx",
            SizeBytes = 7_500_000_000,
            CanSafeDelete = false
        };

        var folderItem = new FreeCleaner.Models.GlobalPackageCacheItem
        {
            Ecosystem = "NuGet (.NET)",
            CacheName = "NuGet Cache",
            FolderPath = @"C:\Users\Test\.nuget\packages",
            SizeBytes = 1024,
            CanSafeDelete = true
        };

        Assert.True(vhdxItem.IsVhdxVirtualDisk);
        Assert.Contains(".vhdx", vhdxItem.SafetyLabel, StringComparison.OrdinalIgnoreCase);
        Assert.False(folderItem.IsVhdxVirtualDisk);

        string script = NextGenForensicsAndRescueService.BuildDiskpartCompactScript(vhdxItem.FolderPath);
        Assert.Contains("select vdisk file=", script);
        Assert.Contains("attach vdisk readonly", script);
        Assert.Contains("compact vdisk", script);
        Assert.Contains("detach vdisk", script);

        var (success, freed, _) = await NextGenForensicsAndRescueService.CompactVhdxVirtualDiskAsync(@"C:\NonExistent\missing_rootfs.vhdx");
        Assert.False(success);
        Assert.Equal(0, freed);
    }

    [Fact]
    public void SmartDiskAdvisorService_ClassifiesProtectedAndSafePathsAccurately()
    {
        Assert.True(SmartDiskAdvisorService.IsProtectedSystemPath(@"C:\Windows\System32"));
        Assert.True(SmartDiskAdvisorService.IsProtectedSystemPath(@"C:\hiberfil.sys"));
        Assert.False(SmartDiskAdvisorService.IsProtectedSystemPath(@"C:\temp\cache_folder"));

        SmartFileAdvice hiberAdvice = SmartDiskAdvisorService.AnalyzePath(@"C:\hiberfil.sys", "hiberfil.sys", isDirectory: false);
        Assert.False(hiberAdvice.CanSafeDeleteToRecycleBin);

        SmartFileAdvice chromeAi = SmartDiskAdvisorService.AnalyzePath(
            @"C:\Users\Test\AppData\Local\Google\Chrome\User Data\OptGuideOnDeviceModel\2025.8.8\weights.bin",
            "weights.bin",
            isDirectory: false);
        Assert.True(chromeAi.CanSafeDeleteToRecycleBin);
        Assert.Contains("AI", chromeAi.BadgeText, StringComparison.OrdinalIgnoreCase);

        SmartFileAdvice claudeVhdx = SmartDiskAdvisorService.AnalyzePath(
            @"C:\Users\Test\AppData\Local\Claude-Data\xyz\vm_bundles\claudevm.bundle\rootfs.vhdx",
            "rootfs.vhdx",
            isDirectory: false);
        Assert.Contains("VHDX", claudeVhdx.BadgeText, StringComparison.OrdinalIgnoreCase);

        SmartFileAdvice devNodeModules = SmartDiskAdvisorService.AnalyzePath(
            @"C:\Projects\App\node_modules",
            "node_modules",
            isDirectory: true);
        Assert.True(devNodeModules.CanSafeDeleteToRecycleBin);
    }

    [Fact]
    public void CleanupHistoryService_RecordsPersistsAndBuilds14DailyBars()
    {
        string tempHistory = Path.Combine(Path.GetTempPath(), $"FreeCleanerHistory_{Guid.NewGuid():N}", "history.json");
        try
        {
            CleanupHistoryService.RecordSession("Akıllı Temizlik", 2_000_000_000, 120, tempHistory);
            CleanupHistoryService.RecordSession("Treemap Güvenli Silme", 500_000_000, 1, tempHistory);

            List<FreeCleaner.Models.CleanupHistoryEntry> loaded = CleanupHistoryService.LoadHistory(tempHistory);
            Assert.Equal(2, loaded.Count);
            Assert.Equal(2_500_000_000, CleanupHistoryService.GetTotalLifetimeFreedBytes(loaded));

            List<FreeCleaner.Models.CleanupHistoryBarItem> bars = CleanupHistoryService.BuildRecentDailyBars(loaded, days: 14, maxBarHeight: 54.0);
            Assert.Equal(14, bars.Count);
            Assert.Equal(2_500_000_000, bars[^1].FreedBytes);
            Assert.True(bars[^1].BarHeight > 4.0);
        }
        finally
        {
            string? dir = Path.GetDirectoryName(tempHistory);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DeepSpaceRecoveryService_ParsesCompactOsAndDismOutputs()
    {
        Assert.True(DeepSpaceRecoveryService.ParseCompactOsQueryOutput("The system is in the Compact state."));
        Assert.True(DeepSpaceRecoveryService.ParseCompactOsQueryOutput("Sistem Compact durumunda."));
        Assert.False(DeepSpaceRecoveryService.ParseCompactOsQueryOutput("The system is not in the Compact state."));
        Assert.False(DeepSpaceRecoveryService.ParseCompactOsQueryOutput("Sistem Compact durumunda değil."));

        string dismSample = "Windows Component Store\nBackups and Disabled Features : 4.21 GB\nReclaimable Packages : 3";
        string summary = DeepSpaceRecoveryService.ParseDismReclaimableSummary(dismSample);
        Assert.Contains("4.21 GB", summary);
    }

    [Fact]
    public async Task SimilarPhotoFinderService_FindsNearIdenticalPhotosAndKeepsHighestResolution()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"FreeCleanerSimilarTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string highResSimilar = Path.Combine(tempDir, "shot_high.png");
            string lowResSimilar = Path.Combine(tempDir, "shot_low.jpg");
            string completelyDifferent = Path.Combine(tempDir, "different_scene.png");

            CreateHorizontalGradientBitmap(highResSimilar, width: 128, height: 128, leftToRightBright: true, isJpeg: false);
            CreateHorizontalGradientBitmap(lowResSimilar, width: 64, height: 64, leftToRightBright: true, isJpeg: true);
            CreateHorizontalGradientBitmap(completelyDifferent, width: 96, height: 96, leftToRightBright: false, isJpeg: false);

            List<FreeCleaner.Models.SimilarPhotoItem> found =
                await SimilarPhotoFinderService.FindSimilarPhotosAsync(tempDir, maxHammingDistance: 6, includeSubfolders: false);

            Assert.Equal(2, found.Count);
            Assert.Equal(found[0].GroupId, found[1].GroupId);
            Assert.DoesNotContain(found, x => x.FileName == "different_scene.png");

            FreeCleaner.Models.SimilarPhotoItem keeper = Assert.Single(found, x => x.FileName == "shot_high.png");
            FreeCleaner.Models.SimilarPhotoItem dup = Assert.Single(found, x => x.FileName == "shot_low.jpg");
            Assert.False(keeper.IsSelected);
            Assert.True(dup.IsSelected);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static void CreateHorizontalGradientBitmap(string path, int width, int height, bool leftToRightBright, bool isJpeg)
    {
        using var bmp = new Drawing.Bitmap(width, height, DrawingImaging.PixelFormat.Format24bppRgb);
        for (int x = 0; x < width; x++)
        {
            int t = (int)Math.Round(x * 255.0 / Math.Max(1, width - 1));
            int val = leftToRightBright ? 255 - t : t;
            Drawing.Color c = Drawing.Color.FromArgb(val, val / 2 + 40, 255 - val / 2);
            for (int y = 0; y < height; y++)
                bmp.SetPixel(x, y, c);
        }

        bmp.Save(path, isJpeg ? DrawingImaging.ImageFormat.Jpeg : DrawingImaging.ImageFormat.Png);
    }

    private static void CreateSyntheticJpegWithExifAndGps(string path, int width, int height)
    {
        byte[] cleanJpegBytes;
        using (var bmp = new Drawing.Bitmap(width, height))
        using (var g = Drawing.Graphics.FromImage(bmp))
        using (var ms = new MemoryStream())
        {
            g.Clear(Drawing.Color.CornflowerBlue);
            bmp.Save(ms, DrawingImaging.ImageFormat.Jpeg);
            cleanJpegBytes = ms.ToArray();
        }

        // Verify JPEG SOI (FF D8) and inject a minimal valid APP1 EXIF+GPS segment right after SOI.
        Assert.True(cleanJpegBytes.Length > 2 && cleanJpegBytes[0] == 0xFF && cleanJpegBytes[1] == 0xD8);

        byte[] app1 = BuildMinimalExifGpsApp1Segment();
        using var outFs = File.Create(path);
        outFs.Write(cleanJpegBytes, 0, 2); // FF D8
        outFs.Write(app1, 0, app1.Length); // APP1
        outFs.Write(cleanJpegBytes, 2, cleanJpegBytes.Length - 2);
    }

    private static byte[] BuildMinimalExifGpsApp1Segment()
    {
        // Build Little-Endian TIFF structure containing IFD0 (Make, Model, GPS pointer) + GPS IFD (GPSLatitudeRef='N').
        var tiff = new byte[84];
        tiff[0] = (byte)'I';
        tiff[1] = (byte)'I';
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), 0x002A);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8); // Offset to IFD0

        // IFD0 at offset 8: 3 entries
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8), 3);

        // Entry 0 (offset 10): Tag 0x010F Make, ASCII(2), count=6, offset=50
        WriteIfdEntry(tiff.AsSpan(10), 0x010F, 2, 6, 50);
        // Entry 1 (offset 22): Tag 0x0110 Model, ASCII(2), count=9, offset=56
        WriteIfdEntry(tiff.AsSpan(22), 0x0110, 2, 9, 56);
        // Entry 2 (offset 34): Tag 0x8825 GPSInfoIFDPointer, LONG(4), count=1, offset=66
        WriteIfdEntry(tiff.AsSpan(34), 0x8825, 4, 1, 66);
        // Next IFD offset = 0 (offset 46..49)
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(46), 0);

        Encoding.ASCII.GetBytes("Canon\0").CopyTo(tiff, 50);
        Encoding.ASCII.GetBytes("EOS Test\0").CopyTo(tiff, 56);
        tiff[65] = 0;

        // GPS IFD at offset 66: 1 entry (Tag 0x0001 GPSLatitudeRef = "N\0")
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(66), 1);
        WriteIfdEntry(tiff.AsSpan(68), 0x0001, 2, 2, 0x0000004E); // 'N', '\0', 0, 0 in little endian
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(80), 0);

        byte[] exifHeader = "Exif\0\0"u8.ToArray();
        int segmentLength = 2 + exifHeader.Length + tiff.Length; // 2 + 6 + 84 = 92

        var segment = new byte[2 + segmentLength];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)segmentLength);
        exifHeader.CopyTo(segment, 4);
        tiff.CopyTo(segment, 4 + exifHeader.Length);
        return segment;
    }

    private static void WriteIfdEntry(Span<byte> dest, ushort tag, ushort type, uint count, uint valueOrOffset)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(dest, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[2..], type);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[4..], count);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[8..], valueOrOffset);
    }

    [Fact]
    public void ShellIntegrationAndProfilesService_ParsesCliArgsAndAppliesProfiles()
    {
        var treemap = ShellIntegrationAndProfilesService.ParseStartupArguments(["--treemap", "\"C:\\Work\""]);
        Assert.Equal(CliLaunchActionKind.OpenTreemap, treemap.Kind);
        Assert.Equal(@"C:\Work", treemap.TargetPath);

        var rootDrive = ShellIntegrationAndProfilesService.ParseStartupArguments(["--treemap", "C:\""]);
        Assert.Equal(CliLaunchActionKind.OpenTreemap, rootDrive.Kind);
        Assert.Equal(@"C:\", rootDrive.TargetPath);

        var exif = ShellIntegrationAndProfilesService.ParseStartupArguments(["--exif=D:\\Photos"]);
        Assert.Equal(CliLaunchActionKind.OpenExifScrubber, exif.Kind);
        Assert.Equal(@"D:\Photos", exif.TargetPath);

        var similar = ShellIntegrationAndProfilesService.ParseStartupArguments(["--similar", @"E:\Shots"]);
        Assert.Equal(CliLaunchActionKind.OpenSimilarPhotos, similar.Kind);
        Assert.Equal(@"E:\Shots", similar.TargetPath);

        Assert.Equal(CliLaunchActionKind.OpenDeepSpace,
            ShellIntegrationAndProfilesService.ParseStartupArguments(["--deep-space"]).Kind);
        Assert.Equal(CliLaunchActionKind.QuickHealthCheck,
            ShellIntegrationAndProfilesService.ParseStartupArguments(["--health-check"]).Kind);
        Assert.Equal(CliLaunchActionKind.ScheduledMaintenance,
            ShellIntegrationAndProfilesService.ParseStartupArguments(["--scheduled-maintenance"]).Kind);

        var rules = new List<FreeCleaner.Models.CleanerRule>
        {
            new() { Id = "win_temp", Name = "Windows Temp", Category = "Sistem", Description = "Geçici dosyalar", IconGlyph = "\uE7F8", Group = FreeCleaner.Models.RuleGroup.System, RiskLevel = FreeCleaner.Models.RiskLevel.Low },
            new() { Id = "browser_cache", Name = "Chrome Cache", Category = "Tarayıcı", Description = "Önbellek", IconGlyph = "\uE774", Group = FreeCleaner.Models.RuleGroup.Browsers, RiskLevel = FreeCleaner.Models.RiskLevel.Low },
            new() { Id = "browser_cookies", Name = "Chrome Cookies", Category = "Tarayıcı", Description = "Çerezler", IconGlyph = "\uE774", Group = FreeCleaner.Models.RuleGroup.Browsers, RiskLevel = FreeCleaner.Models.RiskLevel.High },
            new() { Id = "nuget_cache", Name = "NuGet Cache", Category = "Geliştirici", Description = "Paket önbelleği", IconGlyph = "\uE943", Group = FreeCleaner.Models.RuleGroup.DeveloperAndApps, RiskLevel = FreeCleaner.Models.RiskLevel.Medium },
            new() { Id = "gpu_shader", Name = "NVIDIA DX Shader Cache", Category = "Geliştirici", Description = "Shader", IconGlyph = "\uE7FC", Group = FreeCleaner.Models.RuleGroup.DeveloperAndApps, RiskLevel = FreeCleaner.Models.RiskLevel.Low }
        };

        ShellIntegrationAndProfilesService.ApplyProfileToRules(rules, CleaningProfileKind.SafeDaily);
        Assert.True(rules[0].IsSelected);
        Assert.True(rules[1].IsSelected);
        Assert.False(rules[2].IsSelected);
        Assert.False(rules[3].IsSelected);

        ShellIntegrationAndProfilesService.ApplyProfileToRules(rules, CleaningProfileKind.DeveloperDeep);
        Assert.True(rules[0].IsSelected);
        Assert.True(rules[3].IsSelected);
        Assert.False(rules[2].IsSelected);

        ShellIntegrationAndProfilesService.ApplyProfileToRules(rules, CleaningProfileKind.GamerBoost);
        Assert.True(rules[0].IsSelected);
        Assert.True(rules[4].IsSelected);
        Assert.False(rules[2].IsSelected);

        ShellIntegrationAndProfilesService.ApplyProfileToRules(rules, CleaningProfileKind.MaxPrivacy);
        Assert.True(rules[1].IsSelected);
        Assert.True(rules[2].IsSelected);

        string tempJson = Path.Combine(Path.GetTempPath(), $"fc_cfg_{Guid.NewGuid():N}.json");
        try
        {
            var cfg = new CleanerConfigData
            {
                AutoMonitorEnabled = true,
                AutoMonitorThresholdMb = 2048,
                SelectedTheme = "CyberEmerald",
                SelectedLanguage = "en-US",
                SelectedRuleIds = ["win_temp", "nuget_cache"]
            };

            ShellIntegrationAndProfilesService.ExportSettingsToFile(cfg, tempJson);
            CleanerConfigData restored = ShellIntegrationAndProfilesService.ImportSettingsFromFile(tempJson);

            Assert.True(restored.AutoMonitorEnabled);
            Assert.Equal(2048, restored.AutoMonitorThresholdMb);
            Assert.Equal("CyberEmerald", restored.SelectedTheme);
            Assert.Equal("en-US", restored.SelectedLanguage);
            Assert.Equal(["win_temp", "nuget_cache"], restored.SelectedRuleIds);
        }
        finally
        {
            if (File.Exists(tempJson))
                File.Delete(tempJson);
        }
    }

    [Fact]
    public async Task NtfsHardlinkService_DeduplicatesIdenticalFilesPreservingBothPaths()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"FreeCleanerHardlinkTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string fileA = Path.Combine(tempDir, "master.bin");
            string fileB = Path.Combine(tempDir, "dup_identical.bin");
            string fileC = Path.Combine(tempDir, "dup_mismatch.bin");

            if (!NtfsHardlinkService.IsNtfsSameVolume(fileA, fileB))
                return;

            byte[] payload = new byte[128 * 1024];
            new Random(42).NextBytes(payload);
            await File.WriteAllBytesAsync(fileA, payload);
            await File.WriteAllBytesAsync(fileB, payload);

            byte[] altered = (byte[])payload.Clone();
            altered[0] ^= 0xFF;
            await File.WriteAllBytesAsync(fileC, altered);

            var items = new List<FreeCleaner.Models.LargeOrDuplicateFileItem>
            {
                new() { FileName = "master.bin", Extension = ".bin", FullPath = fileA, SizeBytes = payload.Length, LastModified = DateTime.UtcNow, IsDuplicate = true, DuplicateGroupHash = "GRP1", IsSelected = false },
                new() { FileName = "dup_identical.bin", Extension = ".bin", FullPath = fileB, SizeBytes = payload.Length, LastModified = DateTime.UtcNow, IsDuplicate = true, DuplicateGroupHash = "GRP1", IsSelected = true },
                new() { FileName = "dup_mismatch.bin", Extension = ".bin", FullPath = fileC, SizeBytes = payload.Length, LastModified = DateTime.UtcNow, IsDuplicate = true, DuplicateGroupHash = "GRP1", IsSelected = true }
            };

            HardlinkDeduplicationResult result = await NtfsHardlinkService.DeduplicateWithHardlinksAsync(items);

            Assert.Equal(1, result.LinkedCount);
            Assert.Equal(1, result.SkippedCount);
            Assert.Equal(payload.Length, result.BytesReclaimed);
            Assert.True(File.Exists(fileA));
            Assert.True(File.Exists(fileB));
            Assert.True(File.Exists(fileC));
            Assert.True(NtfsHardlinkService.AreSameNtfsFile(fileA, fileB));
            Assert.False(NtfsHardlinkService.AreSameNtfsFile(fileA, fileC));
            Assert.Equal(payload, await File.ReadAllBytesAsync(fileB));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SystemTrayService_FormatTrayTooltip_StaysWithin63CharLimit()
    {
        string normal = SystemTrayService.FormatTrayTooltip(24.4, 61.8, 142.3);
        Assert.InRange(normal.Length, 10, 63);
        Assert.Contains("CPU %24", normal);
        Assert.Contains("RAM %62", normal);

        string extreme = SystemTrayService.FormatTrayTooltip(999, 999, 999999999999999);
        Assert.True(extreme.Length <= 63);
        Assert.False(string.IsNullOrWhiteSpace(extreme));
    }

    [Fact]
    public void FlagshipTelemetryAndThemeService_EvaluateCriticalAlerts_TriggersAndRespectsCooldown()
    {
        DateTime lastDisk = DateTime.MinValue;
        DateTime lastRam = DateTime.MinValue;
        DateTime t0 = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

        var ok = FlagshipTelemetryAndThemeService.EvaluateCriticalAlerts(70, 80, 120, t0, ref lastDisk, ref lastRam);
        Assert.False(ok.TriggerDiskAlert);
        Assert.False(ok.TriggerRamAlert);
        Assert.Null(ok.Title);

        var first = FlagshipTelemetryAndThemeService.EvaluateCriticalAlerts(95, 92, 12, t0, ref lastDisk, ref lastRam, cooldownMinutes: 30);
        Assert.True(first.TriggerDiskAlert);
        Assert.True(first.TriggerRamAlert);
        Assert.False(string.IsNullOrWhiteSpace(first.Title));
        Assert.Equal(t0, lastDisk);
        Assert.Equal(t0, lastRam);

        var cooled = FlagshipTelemetryAndThemeService.EvaluateCriticalAlerts(96, 94, 10, t0.AddMinutes(5), ref lastDisk, ref lastRam, cooldownMinutes: 30);
        Assert.False(cooled.TriggerDiskAlert);
        Assert.False(cooled.TriggerRamAlert);

        var reFired = FlagshipTelemetryAndThemeService.EvaluateCriticalAlerts(96, 94, 10, t0.AddMinutes(31), ref lastDisk, ref lastRam, cooldownMinutes: 30);
        Assert.True(reFired.TriggerDiskAlert);
        Assert.True(reFired.TriggerRamAlert);
    }

    [Fact]
    public async Task SingleInstanceBridgeService_ForwardsCliArgsOverNamedPipe()
    {
        string token = Guid.NewGuid().ToString("N");
        string mutexName = $"Local\\FreeCleanerTest_{token}";
        string pipeName = $"FreeCleanerPipeTest_{token}";

        using var primary = new SingleInstanceBridgeService();
        Assert.True(primary.TryAcquirePrimaryInstance(mutexName));

        var receivedTcs = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.StartListening(args => receivedTcs.TrySetResult(args), pipeName);

        using var secondary = new SingleInstanceBridgeService();
        Assert.False(secondary.TryAcquirePrimaryInstance(mutexName));

        bool sent = await SingleInstanceBridgeService.SendArgsToPrimaryInstanceAsync(
            ["--treemap", @"C:\Projects"],
            pipeName,
            timeoutMs: 2500);

        Assert.True(sent);
        string[] received = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["--treemap", @"C:\Projects"], received);
    }

    [Fact]
    public void DiskSnapshotDiffService_SavesLoadsAndComputesGrowthDelta()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"FreeCleanerSnapTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            string rootPath = @"C:\SampleDrive\Workspace";
            var baselineEntries = new List<RawTreemapEntry>
            {
                new("Videos", @"C:\SampleDrive\Workspace\Videos", 100_000, IsDirectory: true),
                new("Cache", @"C:\SampleDrive\Workspace\Cache", 200_000, IsDirectory: true)
            };

            DiskFolderSnapshot saved = DiskSnapshotDiffService.SaveSnapshot(rootPath, 300_000, baselineEntries, tempDir);
            DiskFolderSnapshot? loaded = DiskSnapshotDiffService.LoadSnapshot(rootPath, tempDir);

            Assert.NotNull(loaded);
            Assert.Equal(saved.TotalBytes, loaded.TotalBytes);

            var currentEntries = new List<RawTreemapEntry>
            {
                new("Videos", @"C:\SampleDrive\Workspace\Videos", 450_000, IsDirectory: true),
                new("Cache", @"C:\SampleDrive\Workspace\Cache", 180_000, IsDirectory: true),
                new("NewDump.dmp", @"C:\SampleDrive\Workspace\NewDump.dmp", 500_000, IsDirectory: false)
            };

            List<DiskGrowthDiffItem> diff = DiskSnapshotDiffService.ComputeGrowthDiff(loaded, currentEntries, minAbsDeltaBytes: 1024);

            Assert.Equal(3, diff.Count);
            Assert.Equal("NewDump.dmp", diff[0].Name);
            Assert.True(diff[0].IsNew);
            Assert.Equal(500_000, diff[0].DeltaBytes);

            Assert.Equal("Videos", diff[1].Name);
            Assert.False(diff[1].IsNew);
            Assert.Equal(350_000, diff[1].DeltaBytes);
            Assert.StartsWith("+", diff[1].FormattedDelta);

            Assert.Equal("Cache", diff[2].Name);
            Assert.Equal(-20_000, diff[2].DeltaBytes);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ProcessLockGuardService_MapsRulesAndVhdxPathsToExpectedConflicts()
    {
        var rules = new List<FreeCleaner.Models.CleanerRule>
        {
            new() { Id = "chrome_cache", Name = "Google Chrome Cache", Category = "Tarayıcı", Description = "Cache", IconGlyph = "\uE774", Group = FreeCleaner.Models.RuleGroup.Browsers, RiskLevel = FreeCleaner.Models.RiskLevel.Low },
            new() { Id = "discord_cache", Name = "Discord Cache", Category = "App", Description = "Cache", IconGlyph = "\uE943", Group = FreeCleaner.Models.RuleGroup.DeveloperAndApps, RiskLevel = FreeCleaner.Models.RiskLevel.Low },
            new() { Id = "win_temp", Name = "Windows Temp", Category = "Sistem", Description = "Temp", IconGlyph = "\uE7F8", Group = FreeCleaner.Models.RuleGroup.System, RiskLevel = FreeCleaner.Models.RiskLevel.Low }
        };

        List<LockingProcessSpec> ruleSpecs = ProcessLockGuardService.ResolveTargetProcessSpecsForCleanerRules(rules);
        Assert.Contains(ruleSpecs, s => s.ProcessNames.Contains("chrome", StringComparer.OrdinalIgnoreCase));
        Assert.Contains(ruleSpecs, s => s.ProcessNames.Contains("Discord", StringComparer.OrdinalIgnoreCase));

        List<LockingProcessSpec> claudeSpecs = ProcessLockGuardService.ResolveTargetProcessSpecsForVhdxPath(
            @"C:\Users\erkan\AppData\Local\Claude-Data\vm_bundles\claudevm.bundle\rootfs.vhdx");
        Assert.Contains(claudeSpecs, s => s.ProcessNames.Contains("claude", StringComparer.OrdinalIgnoreCase));

        List<LockingProcessSpec> dockerSpecs = ProcessLockGuardService.ResolveTargetProcessSpecsForVhdxPath(
            @"C:\Users\erkan\AppData\Local\Docker\wsl\disk\docker_data.vhdx");
        Assert.Contains(dockerSpecs, s => s.RequiresWslShutdown);

        List<LockingProcessGroup> conflicts = ProcessLockGuardService.DetectRunningConflicts(
            ruleSpecs,
            name => name.Equals("chrome", StringComparison.OrdinalIgnoreCase) ? [101, 202] : []);

        LockingProcessGroup single = Assert.Single(conflicts);
        Assert.Equal("Google Chrome", single.FriendlyName);
        Assert.Equal(2, single.RunningInstanceCount);
    }

    [Fact]
    public void AppUpdateService_ParsesSha256SumsAndBuildsSelfUpdateScript()
    {
        string expectedHash = "a1b2c3d4e5f60718293a4b5c6d7e8f90112233445566778899aabbccddeeff00";
        string sampleSums = $"""
            # Release Checksums
            {expectedHash} *FreeCleaner-Portable-win-x64-v4.1.0.zip
            ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff  OtherTool.exe
            """;

        string? parsed = AppUpdateService.ParseSha256SumsText(sampleSums, "FreeCleaner-Portable-win-x64-v4.1.0.zip");
        Assert.Equal(expectedHash, parsed);
        Assert.Null(AppUpdateService.ParseSha256SumsText(sampleSums, "MissingAsset.exe"));

        string script = AppUpdateService.BuildSafeSelfUpdateScript(98765, @"C:\Apps\FreeCleaner.exe", @"C:\Temp\FreeCleaner.exe");
        Assert.Contains("98765", script);
        Assert.Contains("$backupExe", script);
        Assert.Contains("Start-Process -FilePath $currentExe", script);
    }

    [Fact]
    public async Task AppUpdateService_DownloadAndVerifyUpdateAsync_ValidatesHashAndDeletesTamperedFile()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"FreeCleanerUpdTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            byte[] payload = "FreeCleanerPortableBinaryPayload_v4.1"u8.ToArray();
            string validSha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            string wrongSha = new string('0', 64);

            using var client = new HttpClient(new StubBytesHttpMessageHandler(payload));

            string okFile = Path.Combine(tempDir, "FreeCleaner_ok.exe");
            UpdateDownloadResult okResult = await AppUpdateService.DownloadAndVerifyUpdateAsync(
                "https://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/FreeCleaner.exe",
                okFile,
                validSha,
                httpClient: client);

            Assert.True(okResult.Success);
            Assert.True(okResult.HashVerified);
            Assert.Equal(validSha, okResult.ComputedSha256);
            Assert.True(File.Exists(okFile));

            string tamperedFile = Path.Combine(tempDir, "FreeCleaner_tampered.exe");
            UpdateDownloadResult badResult = await AppUpdateService.DownloadAndVerifyUpdateAsync(
                "https://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/FreeCleaner.exe",
                tamperedFile,
                wrongSha,
                httpClient: client);

            Assert.False(badResult.Success);
            Assert.False(badResult.HashVerified);
            Assert.False(File.Exists(tamperedFile));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class StubBytesHttpMessageHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var msg = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
            msg.Content.Headers.ContentLength = payload.Length;
            return Task.FromResult(msg);
        }
    }

    [Fact]
    public void ShellIntegrationAndProfilesService_ParsesMinimizedAndCombinedCliFlags()
    {
        CliLaunchAction trayOnly = ShellIntegrationAndProfilesService.ParseStartupArguments(["--minimized"]);
        Assert.Equal(CliLaunchActionKind.None, trayOnly.Kind);
        Assert.True(trayOnly.StartMinimized);

        CliLaunchAction combined = ShellIntegrationAndProfilesService.ParseStartupArguments(["--tray", "--treemap", @"D:\Workspace"]);
        Assert.Equal(CliLaunchActionKind.OpenTreemap, combined.Kind);
        Assert.Equal(@"D:\Workspace", combined.TargetPath);
        Assert.True(combined.StartMinimized);
    }

    [Fact]
    public void SystemBoosterAndReportService_BuildDiagnosticHtmlReport_IncludesSvgGaugeAndEscapesLogs()
    {
        var metrics = new SystemMetricsSnapshot(
            OsName: "Windows 11 Pro",
            CpuArchitecture: "X64",
            DotNetVersion: ".NET 10",
            DriveName: @"C:\",
            DriveTotalBytes: 500_000_000_000,
            DriveFreeBytes: 200_000_000_000,
            DriveUsedPercent: 60.0,
            RamTotalBytes: 32_000_000_000,
            RamAvailableBytes: 16_000_000_000,
            RamUsedPercent: 50.0);

        var rules = new List<FreeCleaner.Models.CleanerRule>
        {
            new() { Id = "tmp", Name = "Windows Temp", Category = "Sistem", Description = "Temp", IconGlyph = "\uE7F8", Group = FreeCleaner.Models.RuleGroup.System, DetectedFileCount = 12, DetectedSizeBytes = 1048576 }
        };

        var bars = new List<FreeCleaner.Models.CleanupHistoryBarItem>
        {
            new("21.11", 10_485_760, 48, "10.0 MB", "21 Nov: 10 MB")
        };

        string html = SystemBoosterAndReportService.BuildDiagnosticHtmlReport(
            metrics,
            rules,
            totalFreedBytes: 10_485_760,
            totalDeletedFiles: 12,
            logs: ["<script>alert('xss')</script>"],
            healthScore: 94,
            healthBadge: "A+ Harika",
            lifetimeFreedBytes: 1_073_741_824,
            recentHistory: bars);

        Assert.Contains("<svg", html);
        Assert.Contains("94", html);
        Assert.Contains($"FreeCleaner Pro v{AppUpdateService.CurrentVersion}", html);
        Assert.DoesNotContain("<script>alert('xss')</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void AppUpdateService_CurrentVersionMatches480AndComparesCorrectly()
    {
        Assert.Equal("4.8.0", AppUpdateService.CurrentVersion);
        Assert.True(AppUpdateService.IsNewerVersion(AppUpdateService.CurrentVersion, "v4.9.0"));
        Assert.False(AppUpdateService.IsNewerVersion(AppUpdateService.CurrentVersion, "v4.8.0"));
        Assert.False(AppUpdateService.IsNewerVersion(AppUpdateService.CurrentVersion, "v4.7.0"));
    }

    [Fact]
    public void LocalizationService_ProvidesNonEmptyV41KeysInTurkishAndEnglish()
    {
        string[] requiredKeys =
        [
            "Loc_SettingsAutoStartTrayCheck",
            "Loc_LogFilterHint",
            "Loc_NavDashboard",
            "Loc_CleanerAnalyzeBtn",
            "Loc_StartupHeader",
            "Loc_RegistryScanBtn",
            "Loc_DiskFindLargeBtn",
            "Loc_PkgScanBtn"
        ];

        foreach (string key in requiredKeys)
        {
            string tr = LocalizationService.Get(key, "tr-TR");
            string en = LocalizationService.Get(key, "en-US");
            Assert.False(string.IsNullOrWhiteSpace(tr));
            Assert.False(string.IsNullOrWhiteSpace(en));
            Assert.NotEqual(tr, en);
        }
    }

    [Fact]
    public void AppStoragePathsService_TogglesPortableModeAndExportsSanitizedSupportBundleZip()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerPortTest_{Guid.NewGuid():N}");
        string baseDir = Path.Combine(tempRoot, "app");
        string stdDir = Path.Combine(tempRoot, "appdata");
        string outZip = Path.Combine(tempRoot, "support.zip");

        Directory.CreateDirectory(baseDir);
        Directory.CreateDirectory(stdDir);

        try
        {
            File.WriteAllText(Path.Combine(stdDir, "settings.json"), "{\"SelectedTheme\":\"ObsidianNeon\"}");

            Assert.False(AppStoragePathsService.IsPortableModeEnabled(baseDir));
            var (okOn, _) = AppStoragePathsService.SetPortableMode(true, baseDir, stdDir);
            Assert.True(okOn);
            Assert.True(AppStoragePathsService.IsPortableModeEnabled(baseDir));
            Assert.True(File.Exists(AppStoragePathsService.GetPortableFlagPath(baseDir)));
            Assert.True(File.Exists(Path.Combine(baseDir, "data", "settings.json")));
            Assert.EndsWith("data", AppStoragePathsService.GetDataDirectory(baseDir), StringComparison.OrdinalIgnoreCase);

            var metrics = new SystemMetricsSnapshot("Windows 11", "X64", ".NET 10", @"C:\", 500_000_000_000, 200_000_000_000, 60.0, 32_000_000_000, 16_000_000_000, 50.0);
            var rules = new List<FreeCleaner.Models.CleanerRule>
            {
                new() { Id = "tmp", Name = "Windows Temp", Category = "Sistem", Description = "Temp", IconGlyph = "\uE7F8", Group = FreeCleaner.Models.RuleGroup.System }
            };

            AppStoragePathsService.ExportSupportBundleZip(
                outZip,
                metrics,
                rules,
                totalFreedBytes: 1024,
                totalDeletedFiles: 2,
                logs: ["Auth header: Bearer super_secret_token_123", "Config check token=mySecretKeyValue42"],
                healthScore: 95,
                healthBadge: "A+",
                lifetimeFreedBytes: 4096,
                recentHistory: null,
                dataDirOverride: Path.Combine(baseDir, "data"));

            Assert.True(File.Exists(outZip));
            using (ZipArchive zip = ZipFile.OpenRead(outZip))
            {
                Assert.NotNull(zip.GetEntry("diagnostic_report.html"));
                Assert.NotNull(zip.GetEntry("system_summary.json"));
                Assert.NotNull(zip.GetEntry("settings.json"));

                ZipArchiveEntry? logEntry = zip.GetEntry("activity_sanitized.log");
                Assert.NotNull(logEntry);
                using var reader = new StreamReader(logEntry.Open());
                string logContent = reader.ReadToEnd();
                Assert.DoesNotContain("super_secret_token_123", logContent);
                Assert.DoesNotContain("mySecretKeyValue42", logContent);
                Assert.Contains("***REDACTED***", logContent);
            }

            var (okOff, _) = AppStoragePathsService.SetPortableMode(false, baseDir, stdDir);
            Assert.True(okOff);
            Assert.False(AppStoragePathsService.IsPortableModeEnabled(baseDir));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void StorageForecastService_ComputesDailyGrowthAndDaysTo90Percent()
    {
        const long gb = 1024L * 1024 * 1024;
        long total = 100L * gb;

        var growingSamples = new List<DailyDriveSample>
        {
            new("2026-11-18", 70L * gb, total, 30L * gb),
            new("2026-11-19", 75L * gb, total, 25L * gb),
            new("2026-11-20", 80L * gb, total, 20L * gb),
            new("2026-11-21", 85L * gb, total, 15L * gb)
        };

        StorageForecastSummary summary = StorageForecastService.ComputeForecast(growingSamples, criticalThresholdPercent: 90.0);
        Assert.InRange(summary.AvgDailyGrowthBytes, 4.9 * gb, 5.1 * gb);
        Assert.Equal(1, summary.EstimatedDaysTo90Percent);
        Assert.Equal(4, summary.SampleCount);
        Assert.Contains("~1", summary.HeadlineText);

        string tempFile = Path.Combine(Path.GetTempPath(), $"FreeCleanerForecast_{Guid.NewGuid():N}.json");
        try
        {
            var snap = new SystemMetricsSnapshot("Win11", "X64", ".NET 10", @"C:\", total, 15L * gb, 85.0, 32L * gb, 16L * gb, 50.0);
            List<DailyDriveSample> saved = StorageForecastService.RecordDailySample(snap, tempFile, new DateTime(2026, 11, 21));
            Assert.Single(saved);
            Assert.Equal("2026-11-21", saved[0].DateKey);
            Assert.Single(StorageForecastService.LoadSamples(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void StorageForecastService_HandlesSingleSampleAndStableOrOverThresholdSeries()
    {
        const long gb = 1024L * 1024 * 1024;
        long total = 100L * gb;

        StorageForecastSummary single = StorageForecastService.ComputeForecast([new DailyDriveSample("2026-11-21", 60L * gb, total, 40L * gb)]);
        Assert.Null(single.EstimatedDaysTo90Percent);
        Assert.Equal(1, single.SampleCount);

        StorageForecastSummary stable = StorageForecastService.ComputeForecast(
        [
            new("2026-11-19", 70L * gb, total, 30L * gb),
            new("2026-11-20", 70L * gb, total, 30L * gb),
            new("2026-11-21", 69L * gb, total, 31L * gb)
        ]);
        Assert.Null(stable.EstimatedDaysTo90Percent);

        StorageForecastSummary over = StorageForecastService.ComputeForecast(
        [
            new("2026-11-20", 88L * gb, total, 12L * gb),
            new("2026-11-21", 92L * gb, total, 8L * gb)
        ], criticalThresholdPercent: 90.0);
        Assert.Equal(0, over.EstimatedDaysTo90Percent);
    }

    [Fact]
    public void SmartFolderOrganizerService_PreviewsExecutesAndUndoesFileMovesCleanly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"FreeCleanerOrgTest_{Guid.NewGuid():N}");
        string journalPath = Path.Combine(tempDir, "_journal.json");
        Directory.CreateDirectory(tempDir);

        try
        {
            string[] inputs = ["setup.exe", "report.pdf", "photo.jpg", "clip.mp4", "bundle.zip", "program.cs", "data.xyz", "desktop.lnk"];
            foreach (string f in inputs)
                File.WriteAllText(Path.Combine(tempDir, f), f);

            List<OrganizePlanItem> plan = SmartFolderOrganizerService.BuildOrganizePlan(tempDir);
            Assert.Equal(7, plan.Count);
            Assert.DoesNotContain(plan, p => p.FileName == "desktop.lnk");
            Assert.Contains(plan, p => p.FileName == "setup.exe" && p.TargetCategory == "Kurulumlar");
            Assert.Contains(plan, p => p.FileName == "program.cs" && p.TargetCategory == "Kod");

            var (moved, failed, _) = SmartFolderOrganizerService.ExecutePlan(plan, tempDir, journalPath);
            Assert.Equal(7, moved);
            Assert.Equal(0, failed);
            Assert.True(File.Exists(Path.Combine(tempDir, "Kurulumlar", "setup.exe")));
            Assert.True(File.Exists(Path.Combine(tempDir, "Belgeler", "report.pdf")));
            Assert.True(File.Exists(Path.Combine(tempDir, "desktop.lnk")));
            Assert.True(SmartFolderOrganizerService.HasUndoJournal(journalPath));

            var (restored, undoFailed, _) = SmartFolderOrganizerService.UndoLastOrganize(journalPath);
            Assert.Equal(7, restored);
            Assert.Equal(0, undoFailed);
            Assert.True(File.Exists(Path.Combine(tempDir, "setup.exe")));
            Assert.False(Directory.Exists(Path.Combine(tempDir, "Kurulumlar")));
            Assert.False(SmartFolderOrganizerService.HasUndoJournal(journalPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LocalizationService_CoversAllSidebarNavKeysWithDistinctEnglishAndTurkish()
    {
        string[] navAndFeatureKeys =
        [
            "Loc_SettingsPortableTitle",
            "Loc_DashForecastTitle",
            "Loc_OrgPreviewBtn",
            "Loc_NavDownloads",
            "Loc_NavBloatware",
            "Loc_NavShredder",
            "Loc_NavBooster",
            "Loc_NavPrivacy",
            "Loc_NavNetwork",
            "Loc_NavWingetUpdater",
            "Loc_NavAiModelCleaner",
            "Loc_NavExifScrubber",
            "Loc_NavGitRepoCompressor"
        ];

        foreach (string key in navAndFeatureKeys)
        {
            string tr = LocalizationService.Get(key, "tr-TR");
            string en = LocalizationService.Get(key, "en-US");
            Assert.False(string.IsNullOrWhiteSpace(tr));
            Assert.False(string.IsNullOrWhiteSpace(en));
            Assert.NotEqual(tr, en);
        }
    }
}
