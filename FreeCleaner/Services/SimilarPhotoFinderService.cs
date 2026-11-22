using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using FreeCleaner.Models;
using Drawing = System.Drawing;
using Drawing2D = System.Drawing.Drawing2D;
using DrawingImaging = System.Drawing.Imaging;

namespace FreeCleaner.Services;

/// <summary>
/// Detects visually similar / near-duplicate photos using a zero-dependency 64-bit Perceptual Difference Hash (dHash).
/// </summary>
public static class SimilarPhotoFinderService
{
    private static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff"
    };

    private sealed record HashedPhotoEntry(
        string FileName,
        string FullPath,
        long SizeBytes,
        int Width,
        int Height,
        ulong Hash);

    public static ulong ComputeDifferenceHash(Drawing.Bitmap source)
        => ComputeDifferenceHash((Drawing.Image)source);

    public static ulong ComputeDifferenceHash(Drawing.Image source)
    {
        ArgumentNullException.ThrowIfNull(source);

        (Drawing.RotateFlipType flip, bool swapsDimensions) = GetExifOrientationTransform(source);
        int targetWidth = swapsDimensions ? 8 : 9;
        int targetHeight = swapsDimensions ? 9 : 8;

        using var resized = new Drawing.Bitmap(targetWidth, targetHeight, DrawingImaging.PixelFormat.Format24bppRgb);
        using (var g = Drawing.Graphics.FromImage(resized))
        {
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBilinear;
            g.SmoothingMode = Drawing2D.SmoothingMode.HighSpeed;
            g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, targetWidth, targetHeight);
        }

        if (flip != Drawing.RotateFlipType.RotateNoneFlipNone)
            resized.RotateFlip(flip);

        var rect = new Drawing.Rectangle(0, 0, resized.Width, resized.Height);
        DrawingImaging.BitmapData bmpData = resized.LockBits(
            rect,
            DrawingImaging.ImageLockMode.ReadOnly,
            DrawingImaging.PixelFormat.Format24bppRgb);

        byte[] pixelBytes;
        int stride = Math.Abs(bmpData.Stride);
        int height = resized.Height;
        int width = resized.Width;

        try
        {
            pixelBytes = new byte[stride * height];
            if (bmpData.Stride >= 0)
            {
                Marshal.Copy(bmpData.Scan0, pixelBytes, 0, pixelBytes.Length);
            }
            else
            {
                for (int row = 0; row < height; row++)
                {
                    IntPtr rowPtr = IntPtr.Add(bmpData.Scan0, row * bmpData.Stride);
                    Marshal.Copy(rowPtr, pixelBytes, row * stride, stride);
                }
            }
        }
        finally
        {
            resized.UnlockBits(bmpData);
        }

        ulong hash = 0;
        int bitIndex = 0;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * stride;
            double prevLum = GetLuminanceBgr(pixelBytes, rowOffset);
            for (int x = 1; x < width; x++)
            {
                double curLum = GetLuminanceBgr(pixelBytes, rowOffset + x * 3);
                if (prevLum > curLum)
                    hash |= 1UL << bitIndex;
                prevLum = curLum;
                bitIndex++;
            }
        }
        return hash;
    }

    private static (Drawing.RotateFlipType Flip, bool SwapsDimensions) GetExifOrientationTransform(Drawing.Image source)
    {
        try
        {
            const int exifOrientationId = 0x0112;
            if (Array.IndexOf(source.PropertyIdList, exifOrientationId) < 0)
                return (Drawing.RotateFlipType.RotateNoneFlipNone, false);

            DrawingImaging.PropertyItem? prop = source.GetPropertyItem(exifOrientationId);
            if (prop?.Value is not { Length: >= 2 } val)
                return (Drawing.RotateFlipType.RotateNoneFlipNone, false);

            ushort orientation = BitConverter.ToUInt16(val, 0);
            return orientation switch
            {
                2 => (Drawing.RotateFlipType.RotateNoneFlipX, false),
                3 => (Drawing.RotateFlipType.Rotate180FlipNone, false),
                4 => (Drawing.RotateFlipType.Rotate180FlipX, false),
                5 => (Drawing.RotateFlipType.Rotate90FlipX, true),
                6 => (Drawing.RotateFlipType.Rotate90FlipNone, true),
                7 => (Drawing.RotateFlipType.Rotate270FlipX, true),
                8 => (Drawing.RotateFlipType.Rotate270FlipNone, true),
                _ => (Drawing.RotateFlipType.RotateNoneFlipNone, false)
            };
        }
        catch
        {
            return (Drawing.RotateFlipType.RotateNoneFlipNone, false);
        }
    }

    private static double GetLuminanceBgr(byte[] bgrBytes, int offset)
        => 0.299 * bgrBytes[offset + 2] + 0.587 * bgrBytes[offset + 1] + 0.114 * bgrBytes[offset];

    public static int HammingDistance(ulong a, ulong b)
        => BitOperations.PopCount(a ^ b);

    public static double SimilarityPercent(int hammingDistance)
        => (64 - Math.Clamp(hammingDistance, 0, 64)) * 100.0 / 64.0;

    public static async Task<List<SimilarPhotoItem>> FindSimilarPhotosAsync(
        string folderPath,
        int maxHammingDistance = 8,
        bool includeSubfolders = true,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return [];

        return await Task.Run(async () =>
        {
            var opts = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                RecurseSubdirectories = includeSubfolders
            };

            List<string> candidateFiles = [];
            foreach (string path in Directory.EnumerateFiles(folderPath, "*", opts))
            {
                ct.ThrowIfCancellationRequested();
                if (path.Contains("FreeCleaner_PhotoBackup", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (SupportedImageExtensions.Contains(Path.GetExtension(path)))
                    candidateFiles.Add(path);
            }

            if (candidateFiles.Count < 2)
                return [];

            var hashedBag = new ConcurrentBag<HashedPhotoEntry>();
            await Parallel.ForEachAsync(
                candidateFiles,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8),
                    CancellationToken = ct
                },
                (filePath, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        long fileLength = new FileInfo(filePath).Length;
                        using var fs = new FileStream(
                            filePath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            bufferSize: 65536,
                            FileOptions.SequentialScan);
                        using var img = Drawing.Image.FromStream(fs, useEmbeddedColorManagement: false, validateImageData: false);
                        (_, bool swapsDimensions) = GetExifOrientationTransform(img);
                        ulong hash = ComputeDifferenceHash(img);
                        hashedBag.Add(new HashedPhotoEntry(
                            FileName: Path.GetFileName(filePath),
                            FullPath: filePath,
                            SizeBytes: fileLength,
                            Width: swapsDimensions ? img.Height : img.Width,
                            Height: swapsDimensions ? img.Width : img.Height,
                            Hash: hash));
                    }
                    catch
                    {
                        // Skip corrupt or unsupported images safely.
                    }
                    return ValueTask.CompletedTask;
                });

            List<HashedPhotoEntry> photos = hashedBag
                .OrderBy(p => p.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int n = photos.Count;
            if (n < 2)
                return [];

            int[] parent = new int[n];
            for (int i = 0; i < n; i++) parent[i] = i;

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }

            void Union(int a, int b)
            {
                int ra = Find(a);
                int rb = Find(b);
                if (ra != rb)
                    parent[rb] = ra;
            }

            int threshold = Math.Clamp(maxHammingDistance, 0, 24);
            if (n >= 256)
            {
                var edgeBuckets = new ConcurrentBag<List<(int A, int B)>>();
                Parallel.For(
                    0,
                    n,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 8),
                        CancellationToken = ct
                    },
                    () => new List<(int A, int B)>(),
                    (i, _, localEdges) =>
                    {
                        ulong hi = photos[i].Hash;
                        for (int j = i + 1; j < n; j++)
                        {
                            if (HammingDistance(hi, photos[j].Hash) <= threshold)
                                localEdges.Add((i, j));
                        }
                        return localEdges;
                    },
                    localEdges =>
                    {
                        if (localEdges.Count > 0)
                            edgeBuckets.Add(localEdges);
                    });

                foreach (List<(int A, int B)> bucket in edgeBuckets)
                {
                    foreach ((int a, int b) in bucket)
                        Union(a, b);
                }
            }
            else
            {
                for (int i = 0; i < n; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    ulong hi = photos[i].Hash;
                    for (int j = i + 1; j < n; j++)
                    {
                        if (HammingDistance(hi, photos[j].Hash) <= threshold)
                            Union(i, j);
                    }
                }
            }

            var clusters = new Dictionary<int, List<HashedPhotoEntry>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!clusters.TryGetValue(root, out var list))
                {
                    list = [];
                    clusters[root] = list;
                }
                list.Add(photos[i]);
            }

            var results = new List<SimilarPhotoItem>();
            int groupNo = 1;
            foreach (List<HashedPhotoEntry> cluster in clusters.Values
                .Where(c => c.Count >= 2)
                .OrderBy(c => c.Min(p => p.FullPath), StringComparer.OrdinalIgnoreCase))
            {
                // Keep highest pixel area (tiebreak: largest file size) unselected as primary keeper.
                List<HashedPhotoEntry> ordered = cluster
                    .OrderByDescending(p => (long)p.Width * p.Height)
                    .ThenByDescending(p => p.SizeBytes)
                    .ToList();

                HashedPhotoEntry keeper = ordered[0];
                string groupLabel = $"Grup #{groupNo} ({ordered.Count} fotoğraf)";

                for (int idx = 0; idx < ordered.Count; idx++)
                {
                    HashedPhotoEntry entry = ordered[idx];
                    bool isKeeper = idx == 0;
                    int dist = isKeeper ? 0 : HammingDistance(keeper.Hash, entry.Hash);
                    double sim = SimilarityPercent(dist);

                    results.Add(new SimilarPhotoItem
                    {
                        GroupId = groupNo,
                        GroupLabel = groupLabel,
                        FileName = entry.FileName,
                        FullPath = entry.FullPath,
                        SizeBytes = entry.SizeBytes,
                        Width = entry.Width,
                        Height = entry.Height,
                        SimilarityText = isKeeper ? "Orijinal (%100)" : $"%{sim:F0} Benzer",
                        RoleBadge = isKeeper ? "⭐ En İyi (Korunacak)" : "🗑️ Benzer Kopya",
                        HammingDistance = dist,
                        IsSelected = !isKeeper,
                        StatusText = isKeeper ? "Korunuyor" : "Silinmeye Hazır"
                    });
                }

                groupNo++;
            }

            return results;
        }, ct);
    }

    public static async Task<(int DeletedCount, long FreedBytes)> CleanSelectedSimilarPhotosAsync(
        IEnumerable<SimilarPhotoItem> items,
        bool moveToRecycleBin)
    {
        return await Task.Run(() =>
        {
            int deleted = 0;
            long freed = 0;

            foreach (SimilarPhotoItem item in items.Where(x => x.IsSelected))
            {
                try
                {
                    if (!File.Exists(item.FullPath))
                    {
                        item.StatusText = "Bulunamadı";
                        continue;
                    }

                    bool ok;
                    if (moveToRecycleBin)
                    {
                        ok = RecycleBinHelper.MovePathToRecycleBin(item.FullPath);
                    }
                    else
                    {
                        File.Delete(item.FullPath);
                        ok = true;
                    }

                    if (ok)
                    {
                        deleted++;
                        freed += item.SizeBytes;
                        item.IsSelected = false;
                        item.StatusText = moveToRecycleBin ? "♻️ Geri Dönüşüm'e Taşındı" : "🗑️ Kalıcı Silindi";
                    }
                    else
                    {
                        item.StatusText = "⚠️ Kilitli / Silinemedi";
                    }
                }
                catch (Exception ex)
                {
                    item.StatusText = $"❌ Hata: {ex.Message}";
                }
            }

            return (deleted, freed);
        });
    }
}
