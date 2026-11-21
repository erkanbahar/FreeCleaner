using System.Buffers.Binary;
using System.IO;
using System.Text;
using FreeCleaner.Models;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using DrawingImaging = System.Drawing.Imaging;

namespace FreeCleaner.Services;

public sealed record RawTreemapEntry(string Name, string FullPath, long SizeBytes, bool IsDirectory);

/// <summary>
/// Provides interactive disk treemap layout analysis and lossless/privacy-first photo EXIF/GPS scrubbing.
/// </summary>
public static class VisualDiskAndMediaService
{
    private static readonly string[] TreemapPaletteHex =
    [
        "#2563EB",
        "#0EA5E9",
        "#10B981",
        "#8B5CF6",
        "#F59E0B",
        "#EC4899",
        "#06B6D4",
        "#6366F1",
        "#14B8A6",
        "#F43F5E",
        "#3B82F6",
        "#84CC16"
    ];

    private static readonly HashSet<string> SupportedPhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".tif",
        ".tiff"
    };

    private static readonly EnumerationOptions SafeTopLevelOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        RecurseSubdirectories = false
    };

    // ==================== 1. INTERACTIVE DISK TREEMAP ENGINE ====================

    public static async Task<(List<RawTreemapEntry> RawEntries, List<TreemapNodeItem> Nodes, long TotalBytes, int ScannedCount)> AnalyzeTreemapFolderAsync(
        string rootPath,
        double canvasWidth,
        double canvasHeight,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return ([], [], 0, 0);

        return await Task.Run(async () =>
        {
            var collected = new List<RawTreemapEntry>();
            int scannedCount = 0;

            string[] subDirs = [];
            try
            {
                subDirs = Directory.GetDirectories(rootPath, "*", SafeTopLevelOptions);
            }
            catch
            {
                subDirs = [];
            }

            var dirBag = new System.Collections.Concurrent.ConcurrentBag<RawTreemapEntry>();
            await Parallel.ForEachAsync(
                subDirs,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 6),
                    CancellationToken = ct
                },
                (dir, token) =>
                {
                    long dirBytes = ComputeDirectorySizeSafe(dir, token);
                    Interlocked.Increment(ref scannedCount);
                    if (dirBytes > 0)
                    {
                        string name = Path.GetFileName(dir);
                        if (string.IsNullOrWhiteSpace(name))
                            name = dir;
                        dirBag.Add(new RawTreemapEntry(name, dir, dirBytes, IsDirectory: true));
                    }
                    return ValueTask.CompletedTask;
                });

            collected.AddRange(dirBag);

            try
            {
                foreach (string file in Directory.EnumerateFiles(rootPath, "*", SafeTopLevelOptions))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length > 0)
                        {
                            collected.Add(new RawTreemapEntry(info.Name, info.FullName, info.Length, IsDirectory: false));
                        }
                        scannedCount++;
                    }
                    catch
                    {
                        // Skip inaccessible files.
                    }
                }
            }
            catch
            {
                // Skip inaccessible folder enumeration errors.
            }

            var sorted = collected
                .Where(e => e.SizeBytes > 0)
                .OrderByDescending(e => e.SizeBytes)
                .ToList();

            long totalBytes = sorted.Sum(e => e.SizeBytes);

            List<RawTreemapEntry> rawEntries;
            if (sorted.Count > 24)
            {
                rawEntries = sorted.Take(24).ToList();
                var tail = sorted.Skip(24).ToList();
                long tailBytes = tail.Sum(e => e.SizeBytes);
                if (tailBytes > 0)
                {
                    rawEntries.Add(new RawTreemapEntry(
                        Name: $"Diğer Küçük Öğeler ({tail.Count} öğe)",
                        FullPath: rootPath,
                        SizeBytes: tailBytes,
                        IsDirectory: false));
                }
            }
            else
            {
                rawEntries = sorted;
            }

            var nodes = ComputeTreemapLayout(rawEntries, canvasWidth, canvasHeight);
            return (rawEntries, nodes, totalBytes, scannedCount);
        }, ct);
    }

    private static long ComputeDirectorySizeSafe(string directoryPath, CancellationToken ct)
    {
        long total = 0;
        var stack = new Stack<string>();
        stack.Push(directoryPath);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            string current = stack.Pop();

            try
            {
                foreach (string file in Directory.EnumerateFiles(current, "*", SafeTopLevelOptions))
                {
                    try
                    {
                        total += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // Skip individual locked/unreadable file sizes.
                    }
                }
            }
            catch
            {
                // Skip inaccessible directory files.
            }

            try
            {
                foreach (string sub in Directory.EnumerateDirectories(current, "*", SafeTopLevelOptions))
                {
                    stack.Push(sub);
                }
            }
            catch
            {
                // Skip inaccessible subdirectories.
            }
        }

        return total;
    }

    /// <summary>
    /// Computes a balanced binary-partition treemap layout inside the given viewport bounds.
    /// </summary>
    public static List<TreemapNodeItem> ComputeTreemapLayout(
        IReadOnlyList<RawTreemapEntry> entries,
        double width,
        double height)
    {
        if (entries is null || entries.Count == 0 || width <= 4 || height <= 4)
            return [];

        var valid = entries
            .Where(e => e.SizeBytes > 0)
            .OrderByDescending(e => e.SizeBytes)
            .ToList();

        if (valid.Count == 0)
            return [];

        long totalBytes = valid.Sum(e => e.SizeBytes);
        if (totalBytes <= 0)
            return [];

        var results = new List<TreemapNodeItem>(valid.Count);
        int colorIndex = 0;

        PartitionTreemap(valid, 0, valid.Count, 0, 0, width, height, totalBytes, results, ref colorIndex);
        return results;
    }

    private static void PartitionTreemap(
        List<RawTreemapEntry> items,
        int start,
        int count,
        double x,
        double y,
        double w,
        double h,
        long totalAreaBytes,
        List<TreemapNodeItem> output,
        ref int colorIndex)
    {
        if (count <= 0 || w <= 1 || h <= 1)
            return;

        if (count == 1)
        {
            RawTreemapEntry item = items[start];
            double insetW = Math.Max(2.0, w - 3.0);
            double insetH = Math.Max(2.0, h - 3.0);
            double maxRight = x + w;
            double maxBottom = y + h;
            double drawX = Math.Min(x + 1.5, Math.Max(x, maxRight - insetW));
            double drawY = Math.Min(y + 1.5, Math.Max(y, maxBottom - insetH));
            double share = totalAreaBytes > 0 ? item.SizeBytes * 100.0 / totalAreaBytes : 0.0;

            output.Add(new TreemapNodeItem
            {
                Name = item.Name,
                FullPath = item.FullPath,
                SizeBytes = item.SizeBytes,
                IsDirectory = item.IsDirectory,
                X = Math.Round(drawX, 2),
                Y = Math.Round(drawY, 2),
                Width = Math.Round(insetW, 2),
                Height = Math.Round(insetH, 2),
                FillHex = TreemapPaletteHex[colorIndex++ % TreemapPaletteHex.Length],
                SharePercent = share
            });
            return;
        }

        long subsetBytes = 0;
        for (int i = 0; i < count; i++)
            subsetBytes += items[start + i].SizeBytes;

        if (subsetBytes <= 0)
            return;

        // Split the sorted list so the left group sum is closest to half of subsetBytes.
        long halfTarget = subsetBytes / 2;
        long running = 0;
        int splitCount = 1;
        double bestDiff = double.MaxValue;

        for (int i = 0; i < count - 1; i++)
        {
            running += items[start + i].SizeBytes;
            double diff = Math.Abs(running - halfTarget);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                splitCount = i + 1;
            }
        }

        long firstGroupBytes = 0;
        for (int i = 0; i < splitCount; i++)
            firstGroupBytes += items[start + i].SizeBytes;

        double ratio = Math.Clamp(firstGroupBytes / (double)subsetBytes, 0.01, 0.99);

        if (w >= h)
        {
            double firstW = w * ratio;
            PartitionTreemap(items, start, splitCount, x, y, firstW, h, totalAreaBytes, output, ref colorIndex);
            PartitionTreemap(items, start + splitCount, count - splitCount, x + firstW, y, w - firstW, h, totalAreaBytes, output, ref colorIndex);
        }
        else
        {
            double firstH = h * ratio;
            PartitionTreemap(items, start, splitCount, x, y, w, firstH, totalAreaBytes, output, ref colorIndex);
            PartitionTreemap(items, start + splitCount, count - splitCount, x, y + firstH, w, h - firstH, totalAreaBytes, output, ref colorIndex);
        }
    }

    // ==================== 2. PHOTO EXIF / GPS PRIVACY SCRUBBER ====================

    public static async Task<List<PhotoMetadataItem>> ScanPhotoMetadataAsync(
        string folderPath,
        bool includeSubfolders = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return [];

        return await Task.Run(() =>
        {
            var results = new List<PhotoMetadataItem>();
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                RecurseSubdirectories = includeSubfolders
            };

            foreach (string path in Directory.EnumerateFiles(folderPath, "*", options))
            {
                ct.ThrowIfCancellationRequested();
                string ext = Path.GetExtension(path);
                if (!SupportedPhotoExtensions.Contains(ext))
                    continue;

                try
                {
                    results.Add(InspectSinglePhotoMetadata(path));
                }
                catch
                {
                    // Skip corrupt or unreadable image files.
                }
            }

            return results
                .OrderByDescending(p => p.HasGpsData)
                .ThenByDescending(p => p.TagCount)
                .ThenByDescending(p => p.SizeBytes)
                .ToList();
        }, ct);
    }

    public static PhotoMetadataItem InspectSinglePhotoMetadata(string fullPath)
    {
        byte[] bytes = File.ReadAllBytes(fullPath);
        long sizeBytes = bytes.LongLength;
        string ext = Path.GetExtension(fullPath);

        bool hasGps = false;
        string? make = null;
        string? model = null;
        string? dateTaken = null;
        string? software = null;
        string? artist = null;
        int tagCount = 0;

        using var ms = new MemoryStream(bytes, writable: false);
        using (var img = Drawing.Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false))
        {
            foreach (DrawingImaging.PropertyItem prop in img.PropertyItems)
            {
                // Ignore JPEG internal quantization tables (0x5090 Luminance, 0x5091 Chrominance) added automatically by GDI+.
                if (prop.Id is 0x5090 or 0x5091)
                    continue;

                tagCount++;

                if (prop.Id is (>= 0x0000 and <= 0x001F) or 0x8825)
                    hasGps = true;

                switch (prop.Id)
                {
                    case 0x010F:
                        make = DecodeExifString(prop.Value);
                        break;
                    case 0x0110:
                        model = DecodeExifString(prop.Value);
                        break;
                    case 0x9003:
                        dateTaken = DecodeExifString(prop.Value);
                        break;
                    case 0x0132 when string.IsNullOrWhiteSpace(dateTaken):
                        dateTaken = DecodeExifString(prop.Value);
                        break;
                    case 0x0131:
                        software = DecodeExifString(prop.Value);
                        break;
                    case 0x013B:
                        artist = DecodeExifString(prop.Value);
                        break;
                }
            }
        }

        int pngMetaChunks = ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? CountPngMetadataChunks(bytes)
            : 0;
        tagCount += pngMetaChunks;

        string cameraInfo = (make, model) switch
        {
            (not null, not null) when !model.Contains(make, StringComparison.OrdinalIgnoreCase) => $"{make} {model}",
            (_, not null) => model,
            (not null, _) => make,
            _ => "Bilinmiyor / Temiz"
        };

        var summaryParts = new List<string>();
        if (hasGps) summaryParts.Add("GPS Koordinatı");
        if (!string.IsNullOrWhiteSpace(cameraInfo) && cameraInfo != "Bilinmiyor / Temiz") summaryParts.Add("Kamera Bilgisi");
        if (!string.IsNullOrWhiteSpace(dateTaken)) summaryParts.Add("Çekim Tarihi");
        if (!string.IsNullOrWhiteSpace(software)) summaryParts.Add($"Yazılım: {software}");
        if (!string.IsNullOrWhiteSpace(artist)) summaryParts.Add($"Sahip: {artist}");
        if (pngMetaChunks > 0) summaryParts.Add($"PNG Metadata ({pngMetaChunks})");
        if (summaryParts.Count == 0 && tagCount > 0) summaryParts.Add($"{tagCount} EXIF Etiketi");

        string metadataSummary = summaryParts.Count > 0
            ? string.Join(", ", summaryParts)
            : "Meta veri yok (Temiz)";

        bool hasSensitive = hasGps || tagCount > 0;
        return new PhotoMetadataItem
        {
            FileName = Path.GetFileName(fullPath),
            FullPath = fullPath,
            SizeBytes = sizeBytes,
            HasGpsData = hasGps,
            CameraInfo = cameraInfo,
            DateTaken = string.IsNullOrWhiteSpace(dateTaken) ? "-" : dateTaken,
            MetadataSummary = metadataSummary,
            TagCount = tagCount,
            IsSelected = hasSensitive,
            StatusText = hasSensitive ? "⚠️ Temizlenmeye Hazır" : "✅ Zaten Temiz"
        };
    }

    public static async Task<(bool Success, long BytesSaved, int RemovedTags, string Message)> ScrubPhotoMetadataAsync(
        PhotoMetadataItem item,
        bool createBackup,
        bool optimizeJpegQuality = false,
        int jpegQuality = 88,
        CancellationToken ct = default)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.FullPath) || !File.Exists(item.FullPath))
            return (false, 0, 0, "Dosya bulunamadı.");

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            string fullPath = item.FullPath;
            string ext = Path.GetExtension(fullPath);
            if (!SupportedPhotoExtensions.Contains(ext))
                return (false, 0L, 0, "Desteklenmeyen görüntü formatı.");

            byte[] originalBytes = File.ReadAllBytes(fullPath);
            long originalLength = originalBytes.LongLength;
            int priorTags = item.TagCount;

            if (createBackup)
            {
                string parentDir = Path.GetDirectoryName(fullPath) ?? Path.GetTempPath();
                string backupDir = Path.Combine(parentDir, "FreeCleaner_PhotoBackup");
                Directory.CreateDirectory(backupDir);
                string backupPath = Path.Combine(backupDir, Path.GetFileName(fullPath));
                if (File.Exists(backupPath))
                {
                    string stem = Path.GetFileNameWithoutExtension(fullPath);
                    backupPath = Path.Combine(backupDir, $"{stem}_{DateTime.Now:yyyyMMdd_HHmmss_fff}{ext}");
                }
                File.WriteAllBytes(backupPath, originalBytes);
            }

            ct.ThrowIfCancellationRequested();

            string tmpPath = fullPath + ".fc_scrub_tmp";
            try
            {
                using var ms = new MemoryStream(originalBytes, writable: false);
                using var src = Drawing.Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: true);

                ApplyExifOrientationIfNeeded(src);

                var pixelFormat = ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                    ? DrawingImaging.PixelFormat.Format32bppArgb
                    : DrawingImaging.PixelFormat.Format24bppRgb;

                using var clean = new Drawing.Bitmap(src.Width, src.Height, pixelFormat);
                clean.SetResolution(src.HorizontalResolution > 0 ? src.HorizontalResolution : 96f,
                                    src.VerticalResolution > 0 ? src.VerticalResolution : 96f);

                using (var g = Drawing.Graphics.FromImage(clean))
                {
                    g.CompositingQuality = Drawing2D.CompositingQuality.HighQuality;
                    g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = Drawing2D.SmoothingMode.HighQuality;
                    g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;
                    if (pixelFormat == DrawingImaging.PixelFormat.Format32bppArgb)
                        g.Clear(Drawing.Color.Transparent);
                    g.DrawImage(src, 0, 0, src.Width, src.Height);
                }

                if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    clean.Save(tmpPath, DrawingImaging.ImageFormat.Png);
                }
                else if (ext.Equals(".tif", StringComparison.OrdinalIgnoreCase) || ext.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
                {
                    clean.Save(tmpPath, DrawingImaging.ImageFormat.Tiff);
                }
                else
                {
                    long quality = optimizeJpegQuality ? Math.Clamp(jpegQuality, 60, 100) : 95L;
                    SaveJpegWithQuality(clean, tmpPath, quality);
                }

                File.Move(tmpPath, fullPath, overwrite: true);
                long newLength = new FileInfo(fullPath).Length;
                long saved = Math.Max(0, originalLength - newLength);

                item.StatusText = $"✅ Temizlendi ({priorTags} etiket silindi)";
                return (true, saved, priorTags, item.StatusText);
            }
            catch (Exception ex)
            {
                try
                {
                    if (File.Exists(tmpPath))
                        File.Delete(tmpPath);
                }
                catch
                {
                    // Ignore temp cleanup failures.
                }

                item.StatusText = $"❌ Hata: {ex.Message}";
                return (false, 0L, 0, ex.Message);
            }
        }, ct);
    }

    private static void ApplyExifOrientationIfNeeded(Drawing.Image img)
    {
        DrawingImaging.PropertyItem? orientationProp = img.PropertyItems.FirstOrDefault(p => p.Id == 0x0112);
        if (orientationProp?.Value is not { Length: >= 2 } val)
            return;

        ushort orientation = BitConverter.ToUInt16(val, 0);
        var flip = orientation switch
        {
            2 => Drawing.RotateFlipType.RotateNoneFlipX,
            3 => Drawing.RotateFlipType.Rotate180FlipNone,
            4 => Drawing.RotateFlipType.Rotate180FlipX,
            5 => Drawing.RotateFlipType.Rotate90FlipX,
            6 => Drawing.RotateFlipType.Rotate90FlipNone,
            7 => Drawing.RotateFlipType.Rotate270FlipX,
            8 => Drawing.RotateFlipType.Rotate270FlipNone,
            _ => Drawing.RotateFlipType.RotateNoneFlipNone
        };

        if (flip != Drawing.RotateFlipType.RotateNoneFlipNone)
            img.RotateFlip(flip);
    }

    private static void SaveJpegWithQuality(Drawing.Bitmap bitmap, string outputPath, long qualityLevel)
    {
        DrawingImaging.ImageCodecInfo? jpegCodec = DrawingImaging.ImageCodecInfo
            .GetImageEncoders()
            .FirstOrDefault(c => c.MimeType?.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) == true);

        if (jpegCodec is null)
        {
            bitmap.Save(outputPath, DrawingImaging.ImageFormat.Jpeg);
            return;
        }

        using var encoderParams = new DrawingImaging.EncoderParameters(1);
        using var qualityParam = new DrawingImaging.EncoderParameter(DrawingImaging.Encoder.Quality, qualityLevel);
        encoderParams.Param[0] = qualityParam;
        bitmap.Save(outputPath, jpegCodec, encoderParams);
    }

    private static string? DecodeExifString(byte[]? raw)
    {
        if (raw is null || raw.Length == 0)
            return null;

        string decoded = Encoding.UTF8.GetString(raw).Trim('\0', ' ', '\r', '\n');
        return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
    }

    private static int CountPngMetadataChunks(byte[] pngBytes)
    {
        if (pngBytes.Length < 8)
            return 0;

        int count = 0;
        int offset = 8; // Skip 8-byte PNG signature
        while (offset + 12 <= pngBytes.Length)
        {
            uint chunkLen = BinaryPrimitives.ReadUInt32BigEndian(pngBytes.AsSpan(offset, 4));
            string chunkType = Encoding.ASCII.GetString(pngBytes, offset + 4, 4);
            if (chunkType is "eXIf" or "tEXt" or "iTXt" or "zTXt")
                count++;

            if (chunkType == "IEND")
                break;

            long next = offset + 12L + chunkLen;
            if (next <= offset || next > pngBytes.Length)
                break;

            offset = (int)next;
        }

        return count;
    }
}
