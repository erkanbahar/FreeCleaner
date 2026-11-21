using System.Collections.Concurrent;
using System.IO;
using System.Numerics;
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
    {
        using var resized = new Drawing.Bitmap(9, 8, DrawingImaging.PixelFormat.Format24bppRgb);
        using (var g = Drawing.Graphics.FromImage(resized))
        {
            g.InterpolationMode = Drawing2D.InterpolationMode.HighQualityBilinear;
            g.SmoothingMode = Drawing2D.SmoothingMode.HighSpeed;
            g.PixelOffsetMode = Drawing2D.PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, 9, 8);
        }

        ulong hash = 0;
        int bitIndex = 0;
        for (int y = 0; y < 8; y++)
        {
            double prevLum = GetLuminance(resized.GetPixel(0, y));
            for (int x = 1; x < 9; x++)
            {
                double curLum = GetLuminance(resized.GetPixel(x, y));
                if (prevLum > curLum)
                    hash |= 1UL << bitIndex;
                prevLum = curLum;
                bitIndex++;
            }
        }
        return hash;
    }

    private static double GetLuminance(Drawing.Color c)
        => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

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
                        byte[] bytes = File.ReadAllBytes(filePath);
                        using var ms = new MemoryStream(bytes, writable: false);
                        using var img = Drawing.Image.FromStream(ms, useEmbeddedColorManagement: false, validateImageData: false);
                        using var bmp = new Drawing.Bitmap(img);
                        ulong hash = ComputeDifferenceHash(bmp);
                        hashedBag.Add(new HashedPhotoEntry(
                            FileName: Path.GetFileName(filePath),
                            FullPath: filePath,
                            SizeBytes: bytes.LongLength,
                            Width: bmp.Width,
                            Height: bmp.Height,
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
            foreach (List<HashedPhotoEntry> cluster in clusters.Values.Where(c => c.Count >= 2))
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
