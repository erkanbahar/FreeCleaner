using System.Buffers.Binary;
using System.IO;
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
}
