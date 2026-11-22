using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using FreeCleaner.Models;
using Microsoft.Win32;

namespace FreeCleaner.Services;

public static class EighthGenAiAndSecuritySuiteService
{
    // ==================== 1. LOCAL AI / LLM MODEL & CACHE HUNTER ====================

    private static readonly HashSet<string> AiWeightExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gguf", ".safetensors", ".onnx", ".pt", ".pth", ".ckpt", ".tflite", ".ggml"
    };

    public static bool IsStandaloneAiWeightFile(string filePath, long fileSizeBytes)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        string fileName = Path.GetFileName(filePath);
        if (fileName.Equals("weights.bin", StringComparison.OrdinalIgnoreCase))
            return fileSizeBytes >= 50L * 1024 * 1024;

        string ext = Path.GetExtension(filePath);
        return AiWeightExtensions.Contains(ext) && fileSizeBytes >= 10L * 1024 * 1024;
    }

    public static async Task<List<AiModelCacheItem>> ScanAiModelCachesAsync(string? customSearchDir = null)
    {
        return await Task.Run(() =>
        {
            var results = new List<AiModelCacheItem>();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            var knownAiDirs = new (string Framework, string Title, string Path)[]
            {
                ("Ollama LLM", "Ollama Yerel LLM Modelleri (~/.ollama/models)", Path.Combine(userProfile, ".ollama", "models")),
                ("HuggingFace Hub", "HuggingFace Transformers & Diffusers Önbelleği", Path.Combine(userProfile, ".cache", "huggingface", "hub")),
                ("HuggingFace Datasets", "HuggingFace Veri Seti Önbelleği", Path.Combine(userProfile, ".cache", "huggingface", "datasets")),
                ("LM Studio", "LM Studio İndirilen GGUF Modelleri (~/.lmstudio/models)", Path.Combine(userProfile, ".lmstudio", "models")),
                ("LM Studio", "LM Studio Önbelleği (~/.cache/lm-studio)", Path.Combine(userProfile, ".cache", "lm-studio")),
                ("PyTorch / TorchHub", "PyTorch Hub & TorchInductor Önbelleği", Path.Combine(userProfile, ".cache", "torch")),
                ("OpenAI Whisper", "Whisper Ses Tanıma Model Önbelleği", Path.Combine(userProfile, ".cache", "whisper")),
                ("Matplotlib / SciPy", "Bilimsel Python & AI Grafik Önbelleği", Path.Combine(userProfile, ".matplotlib")),
                ("Ultralytics YOLO", "YOLO Görsel AI Model Önbelleği", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ultralytics")),
                ("UV Python AI", "Astral UV Paket & AI Wheel Önbelleği", Path.Combine(localAppData, "uv", "cache")),
                ("Google Chrome AI", "Chrome Cihaz İçi Gemini Nano Modeli (OptGuideOnDeviceModel)", Path.Combine(localAppData, "Google", "Chrome", "User Data", "OptGuideOnDeviceModel")),
                ("Chrome Beta AI", "Chrome Beta Cihaz İçi AI Modeli (OptGuideOnDeviceModel)", Path.Combine(localAppData, "Google", "Chrome Beta", "User Data", "OptGuideOnDeviceModel")),
                ("Chrome Canary AI", "Chrome Canary Cihaz İçi AI Modeli (OptGuideOnDeviceModel)", Path.Combine(localAppData, "Google", "Chrome SxS", "User Data", "OptGuideOnDeviceModel")),
                ("Microsoft Edge AI", "Edge Cihaz İçi AI Modeli (OptGuideOnDeviceModel)", Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "OptGuideOnDeviceModel")),
                ("Microsoft Edge AI", "Edge Phi Yerel AI Modeli (EdgeOnDeviceModel)", Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "EdgeOnDeviceModel")),
                ("Brave Browser AI", "Brave Cihaz İçi AI Modeli (OptGuideOnDeviceModel)", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "OptGuideOnDeviceModel")),
                ("Vivaldi Browser AI", "Vivaldi Cihaz İçi AI Modeli (OptGuideOnDeviceModel)", Path.Combine(localAppData, "Vivaldi", "User Data", "OptGuideOnDeviceModel")),
                ("Opera Browser AI", "Opera Cihaz İçi AI Modeli (OptGuideOnDeviceModel)", Path.Combine(localAppData, "Opera Software", "Opera Stable", "OptGuideOnDeviceModel"))
            };

            foreach (var (framework, title, dirPath) in knownAiDirs)
            {
                try
                {
                    if (!Directory.Exists(dirPath)) continue;
                    long bytes = CalculateDirectorySizeSafe(dirPath);
                    if (bytes <= 0) continue;

                    results.Add(new AiModelCacheItem
                    {
                        AiFramework = framework,
                        ModelOrCacheName = title,
                        FullPath = dirPath,
                        SizeBytes = bytes,
                        IsDirectory = true,
                        IsSelected = false
                    });
                }
                catch { }
            }

            // Detect Claude Desktop Local VM Bundles (Claude-Data\...\vm_bundles\claudevm.bundle\rootfs.vhdx)
            foreach (string claudeRoot in new[]
            {
                Path.Combine(localAppData, "Claude-Data"),
                Path.Combine(localAppData, "AnthropicClaude")
            })
            {
                try
                {
                    if (!Directory.Exists(claudeRoot)) continue;
                    foreach (string vmDir in Directory.EnumerateDirectories(claudeRoot, "vm_bundles", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        MaxRecursionDepth = 2
                    }))
                    {
                        long bytes = CalculateDirectorySizeSafe(vmDir);
                        if (bytes <= 0) continue;

                        results.Add(new AiModelCacheItem
                        {
                            AiFramework = "Claude Desktop VM",
                            ModelOrCacheName = "Claude Yerel VM Paketi (rootfs.vhdx — Claude kapalıyken silin)",
                            FullPath = vmDir,
                            SizeBytes = bytes,
                            IsDirectory = true,
                            IsSelected = false
                        });
                    }
                }
                catch { }
            }

            // Scan custom folder or Downloads/UserProfile for standalone .gguf / .safetensors / .onnx / weights.bin model files
            var fileSearchRoots = new List<string>();
            if (!string.IsNullOrWhiteSpace(customSearchDir) && Directory.Exists(customSearchDir))
                fileSearchRoots.Add(customSearchDir);

            string downloads = Path.Combine(userProfile, "Downloads");
            if (Directory.Exists(downloads) && !fileSearchRoots.Contains(downloads, StringComparer.OrdinalIgnoreCase))
                fileSearchRoots.Add(downloads);

            foreach (var root in fileSearchRoots)
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(root, "*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        MaxRecursionDepth = 4
                    }))
                    {
                        var fi = new FileInfo(file);
                        if (!IsStandaloneAiWeightFile(fi.FullName, fi.Length))
                            continue;

                        string ext = Path.GetExtension(fi.Name);
                        string badge = fi.Name.Equals("weights.bin", StringComparison.OrdinalIgnoreCase)
                            ? "AI Model Ağırlığı (WEIGHTS.BIN)"
                            : $"AI Model Ağırlığı ({ext.ToUpperInvariant()})";

                        results.Add(new AiModelCacheItem
                        {
                            AiFramework = badge,
                            ModelOrCacheName = fi.Name,
                            FullPath = fi.FullName,
                            SizeBytes = fi.Length,
                            IsDirectory = false,
                            IsSelected = false
                        });
                    }
                }
                catch { }
            }

            return results.OrderByDescending(x => x.SizeBytes).ToList();
        });
    }

    public static async Task<(int CleanedCount, long FreedBytes)> CleanSelectedAiCachesAsync(IEnumerable<AiModelCacheItem> items)
    {
        return await Task.Run(() =>
        {
            int count = 0;
            long freed = 0;

            foreach (var item in items.Where(x => x.IsSelected))
            {
                try
                {
                    if (item.IsDirectory && Directory.Exists(item.FullPath))
                    {
                        Directory.Delete(item.FullPath, recursive: true);
                        count++;
                        freed += item.SizeBytes;
                    }
                    else if (!item.IsDirectory && File.Exists(item.FullPath))
                    {
                        File.Delete(item.FullPath);
                        count++;
                        freed += item.SizeBytes;
                    }
                }
                catch { }
            }

            return (count, freed);
        });
    }

    private static long CalculateDirectorySizeSafe(string dir)
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            }))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return total;
    }

    // ==================== 2. WINDOWS FIREWALL APP INTERNET BLOCKER ====================

    public static async Task<List<FirewallBlockRuleItem>> GetFirewallBlockRulesAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<FirewallBlockRuleItem>();
            try
            {
                string ps = @"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Get-NetFirewallRule -Action Block -ErrorAction SilentlyContinue | Select-Object -First 80 | ForEach-Object {
    $app = ($_ | Get-NetFirewallApplicationFilter -ErrorAction SilentlyContinue).Program
    if (-not $app) { $app = 'Tüm Programlar / Port Kuralı' }
    Write-Output ""$($_.DisplayName)|$($_.Direction)|$($_.Enabled)|$app|$($_.Profile)""
}";
                ProcessStartInfo psi = SafeProcessRunner.BuildEncodedPowerShellStartInfo(ps, runAsAdmin: false, redirectOutput: true);
                ProcessRunResult runResult = SafeProcessRunner.Run(psi, TimeSpan.FromSeconds(10));
                if (runResult.Started && !runResult.TimedOut)
                {
                    string output = runResult.StandardOutput;

                    foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split('|');
                        if (parts.Length < 5) continue;

                        string ruleName = parts[0].Trim();
                        string dir = parts[1].Trim().Equals("Outbound", StringComparison.OrdinalIgnoreCase)
                            ? "Giden Bağlantı (Outbound)"
                            : "Gelen Bağlantı (Inbound)";
                        string enabled = parts[2].Trim().Equals("True", StringComparison.OrdinalIgnoreCase)
                            ? "⛔ İNTERNET ENGELLİ (Aktif)"
                            : "⚪ Pasif Kural";

                        list.Add(new FirewallBlockRuleItem
                        {
                            RuleName = ruleName,
                            Direction = dir,
                            ActionBadge = enabled,
                            ProgramPath = parts[3].Trim(),
                            ProfileText = parts[4].Trim(),
                            IsSelected = ruleName.StartsWith("FreeCleaner_Block_", StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }
            }
            catch { }

            return list
                .OrderByDescending(x => x.RuleName.StartsWith("FreeCleaner_Block_", StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x.RuleName)
                .ToList();
        });
    }

    public static async Task<(bool Success, string Message)> BlockExecutableInFirewallAsync(string exeFullPath)
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(exeFullPath) || !File.Exists(exeFullPath))
                return (false, "Lütfen geçerli bir .exe uygulama dosyası seçin.");

            try
            {
                string appName = Path.GetFileNameWithoutExtension(exeFullPath);
                string ruleName = $"FreeCleaner_Block_{appName}";

                string script = SafeProcessRunner.BuildFirewallBlockScript(ruleName, exeFullPath);
                ProcessStartInfo psi = SafeProcessRunner.BuildEncodedPowerShellStartInfo(script, runAsAdmin: true);
                using var proc = Process.Start(psi);
                proc?.WaitForExit(10000);

                return (true, $"'{appName}.exe' uygulamasının gelen ve giden tüm internet erişimi Güvenlik Duvarı (Firewall) üzerinden engellendi!");
            }
            catch (Exception ex)
            {
                return (false, $"Firewall kuralı eklenemedi: {ex.Message}");
            }
        });
    }

    public static async Task<(int RemovedCount, string Message)> RemoveSelectedFirewallRulesAsync(IEnumerable<FirewallBlockRuleItem> items)
    {
        return await Task.Run(() =>
        {
            var selected = items.Where(x => x.IsSelected).ToList();
            if (selected.Count == 0)
                return (0, "Kaldırılacak güvenlik duvarı kuralı seçilmedi.");

            try
            {
                string script = SafeProcessRunner.BuildFirewallDeleteScript(selected.Select(r => r.RuleName));
                ProcessStartInfo psi = SafeProcessRunner.BuildEncodedPowerShellStartInfo(script, runAsAdmin: true);
                using var proc = Process.Start(psi);
                proc?.WaitForExit(10000);

                return (selected.Count, $"{selected.Count} Güvenlik Duvarı engelleme kuralı kaldırıldı ve uygulamanın internet erişimi açıldı.");
            }
            catch (Exception ex)
            {
                return (0, $"Kural silinemedi: {ex.Message}");
            }
        });
    }

    // ==================== 3. AES-256-GCM FILE VAULT & MULTI-HASH VERIFIER ====================

    private static readonly byte[] VaultMagicHeaderV1 = "FCVAULT1"u8.ToArray();
    private static readonly byte[] VaultMagicHeaderV2 = "FCVAULT2"u8.ToArray();
    private const int VaultV1Iterations = 100_000;
    private const int VaultV2Iterations = 600_000;
    public const int DefaultVaultChunkSizeBytes = 1024 * 1024;
    private const int VaultV2HeaderLength = 8 + 4 + 4 + 16 + 12;
    private const int VaultTagLength = 16;

    private static void DeriveChunkNonce(ReadOnlySpan<byte> baseNonce, ulong chunkIndex, Span<byte> destination)
    {
        baseNonce.CopyTo(destination);
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(counterBytes, chunkIndex);
        for (int i = 0; i < 8; i++)
            destination[4 + i] ^= counterBytes[i];
    }

    private static void BuildChunkAad(ReadOnlySpan<byte> header44, ulong chunkIndex, bool isFinalChunk, Span<byte> aad53)
    {
        header44.CopyTo(aad53);
        BinaryPrimitives.WriteUInt64LittleEndian(aad53[44..52], chunkIndex);
        aad53[52] = isFinalChunk ? (byte)1 : (byte)0;
    }

    public static async Task<List<FileHashAndVaultResultItem>> ComputeMultiHashesAsync(string filePath, string? expectedHash = null)
    {
        return await Task.Run(() =>
        {
            var results = new List<FileHashAndVaultResultItem>();
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return results;

            string fileName = Path.GetFileName(filePath);
            string cleanExpected = (expectedHash ?? string.Empty).Trim().Replace(" ", "").ToUpperInvariant();

            string MatchStatus(string computed)
            {
                if (string.IsNullOrEmpty(cleanExpected))
                    return "ℹ️ Referans Hash Girilmedi";
                return string.Equals(computed, cleanExpected, StringComparison.OrdinalIgnoreCase)
                    ? "✅ EŞLEŞTİ! (Dosya Orijinal & Güvenli)"
                    : "🚨 EŞLEŞMEDİ! (Farklı Hash)";
            }

            try
            {
                using var fs1 = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                // Multi-hash verifier intentionally outputs legacy digests alongside SHA-256/SHA-512 for external checksum matching.
#pragma warning disable CA5351
                string md5 = Convert.ToHexString(MD5.HashData(fs1));
#pragma warning restore CA5351
                results.Add(new FileHashAndVaultResultItem
                {
                    AlgorithmOrOperation = "MD5 (128-bit)",
                    FileName = fileName,
                    HashOrOutputValue = md5,
                    VerificationStatus = MatchStatus(md5)
                });

                fs1.Position = 0;
#pragma warning disable CA5350
                string sha1 = Convert.ToHexString(SHA1.HashData(fs1));
#pragma warning restore CA5350
                results.Add(new FileHashAndVaultResultItem
                {
                    AlgorithmOrOperation = "SHA-1 (160-bit)",
                    FileName = fileName,
                    HashOrOutputValue = sha1,
                    VerificationStatus = MatchStatus(sha1)
                });

                fs1.Position = 0;
                string sha256 = Convert.ToHexString(SHA256.HashData(fs1));
                results.Add(new FileHashAndVaultResultItem
                {
                    AlgorithmOrOperation = "SHA-256 (256-bit)",
                    FileName = fileName,
                    HashOrOutputValue = sha256,
                    VerificationStatus = MatchStatus(sha256)
                });

                fs1.Position = 0;
                string sha512 = Convert.ToHexString(SHA512.HashData(fs1));
                results.Add(new FileHashAndVaultResultItem
                {
                    AlgorithmOrOperation = "SHA-512 (512-bit)",
                    FileName = fileName,
                    HashOrOutputValue = sha512,
                    VerificationStatus = MatchStatus(sha512)
                });
            }
            catch { }

            return results;
        });
    }

    public static async Task<(bool Success, string OutputFilePath, string Message)> EncryptFileAes256GcmAsync(
        string inputFilePath,
        string password,
        int chunkSizeBytes = DefaultVaultChunkSizeBytes)
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(inputFilePath) || !File.Exists(inputFilePath))
            {
                return (false, string.Empty, LocalizationService.GetText(
                    "Şifrelenecek geçerli bir dosya seçin.",
                    "Select a valid file to encrypt."));
            }

            if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
            {
                return (false, string.Empty, LocalizationService.GetText(
                    "Lütfen en az 4 karakterli bir şifreleme parolası girin.",
                    "Enter an encryption password of at least 4 characters."));
            }

            int safeChunkSize = Math.Clamp(chunkSizeBytes, 1024, 16 * 1024 * 1024);
            string outPath = inputFilePath + ".fcvault";
            string tempOutPath = $"{outPath}.{Guid.NewGuid():N}.tmp";

            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[] key = new byte[32];
            byte[] plainBuf = new byte[safeChunkSize];
            byte[] cipherBuf = new byte[safeChunkSize];

            try
            {
                byte[] salt = RandomNumberGenerator.GetBytes(16);
                byte[] baseNonce = RandomNumberGenerator.GetBytes(12);

                byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(
                    passwordBytes,
                    salt,
                    iterations: VaultV2Iterations,
                    HashAlgorithmName.SHA256,
                    outputLength: 32);
                Buffer.BlockCopy(derivedKey, 0, key, 0, 32);
                CryptographicOperations.ZeroMemory(derivedKey);

                byte[] header = new byte[VaultV2HeaderLength];
                VaultMagicHeaderV2.CopyTo(header, 0);
                BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), VaultV2Iterations);
                BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12, 4), safeChunkSize);
                salt.CopyTo(header, 16);
                baseNonce.CopyTo(header, 32);

                using (var aes = new AesGcm(key, VaultTagLength))
                using (var inFs = new FileStream(inputFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan))
                using (var outFs = new FileStream(tempOutPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
                {
                    outFs.Write(header);

                    Span<byte> chunkLenBytes = stackalloc byte[4];
                    Span<byte> tag = stackalloc byte[VaultTagLength];
                    Span<byte> chunkNonce = stackalloc byte[12];
                    Span<byte> aad = stackalloc byte[53];

                    long totalInputLength = inFs.Length;
                    ulong chunkIndex = 0;

                    if (totalInputLength == 0)
                    {
                        DeriveChunkNonce(baseNonce, chunkIndex, chunkNonce);
                        BuildChunkAad(header, chunkIndex, isFinalChunk: true, aad);
                        aes.Encrypt(chunkNonce, ReadOnlySpan<byte>.Empty, Span<byte>.Empty, tag, aad);

                        BinaryPrimitives.WriteInt32LittleEndian(chunkLenBytes, 0);
                        outFs.Write(chunkLenBytes);
                        outFs.Write(tag);
                    }
                    else
                    {
                        long totalRead = 0;
                        while (totalRead < totalInputLength)
                        {
                            int read = inFs.ReadAtLeast(plainBuf, safeChunkSize, throwOnEndOfStream: false);
                            if (read <= 0)
                                break;

                            totalRead += read;
                            bool isFinalChunk = totalRead >= totalInputLength;

                            DeriveChunkNonce(baseNonce, chunkIndex, chunkNonce);
                            BuildChunkAad(header, chunkIndex, isFinalChunk, aad);
                            aes.Encrypt(chunkNonce, plainBuf.AsSpan(0, read), cipherBuf.AsSpan(0, read), tag, aad);

                            BinaryPrimitives.WriteInt32LittleEndian(chunkLenBytes, read);
                            outFs.Write(chunkLenBytes);
                            outFs.Write(tag);
                            outFs.Write(cipherBuf.AsSpan(0, read));

                            chunkIndex++;
                        }
                    }

                    outFs.Flush(flushToDisk: true);
                }

                File.Move(tempOutPath, outPath, overwrite: true);

                return (true, outPath, LocalizationService.GetText(
                    $"Dosya AES-256-GCM v2 (PBKDF2 600.000 İterasyon) ile şifrelendi: {Path.GetFileName(outPath)}",
                    $"File encrypted with AES-256-GCM v2 (PBKDF2 600,000 iterations): {Path.GetFileName(outPath)}"));
            }
            catch (Exception ex)
            {
                ErrorLogService.Log("CryptoVault:Encrypt", ex);
                return (false, string.Empty, LocalizationService.GetText(
                    $"Şifreleme hatası: {ex.Message}",
                    $"Encryption error: {ex.Message}"));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plainBuf);
                try
                {
                    if (File.Exists(tempOutPath))
                        File.Delete(tempOutPath);
                }
                catch
                {
                    // Ignore cleanup error.
                }
            }
        });
    }

    public static async Task<(bool Success, string OutputFilePath, string Message)> DecryptFileAes256GcmAsync(string vaultFilePath, string password)
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(vaultFilePath) || !File.Exists(vaultFilePath))
            {
                return (false, string.Empty, LocalizationService.GetText(
                    "Şifresi çözülecek .fcvault dosyasını seçin.",
                    "Select a .fcvault file to decrypt."));
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                return (false, string.Empty, LocalizationService.GetText(
                    "Lütfen şifre çözme parolasını girin.",
                    "Please enter the decryption password."));
            }

            string outPath = vaultFilePath.EndsWith(".fcvault", StringComparison.OrdinalIgnoreCase)
                ? vaultFilePath[..^8]
                : vaultFilePath + ".decrypted";

            if (File.Exists(outPath))
            {
                string dir = Path.GetDirectoryName(outPath) ?? string.Empty;
                string nameNoExt = Path.GetFileNameWithoutExtension(outPath);
                string ext = Path.GetExtension(outPath);
                outPath = Path.Combine(dir, $"{nameNoExt}_Restored{ext}");
            }

            string tempOutPath = $"{outPath}.{Guid.NewGuid():N}.tmp";
            byte[] passwordBytes = Encoding.UTF8.GetBytes(password);
            byte[] key = new byte[32];
            byte[]? plainBuf = null;

            try
            {
                using var inFs = new FileStream(vaultFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
                if (inFs.Length < 8)
                {
                    return (false, string.Empty, LocalizationService.GetText(
                        "Geçersiz veya bozuk .fcvault dosyası.",
                        "Invalid or corrupted .fcvault file."));
                }

                byte[] magic = new byte[8];
                inFs.ReadExactly(magic);

                if (magic.AsSpan().SequenceEqual(VaultMagicHeaderV2))
                {
                    if (inFs.Length < VaultV2HeaderLength + 4 + VaultTagLength)
                    {
                        return (false, string.Empty, LocalizationService.GetText(
                            "Geçersiz veya eksik FCVAULT2 dosyası.",
                            "Invalid or truncated FCVAULT2 vault file."));
                    }

                    byte[] header = new byte[VaultV2HeaderLength];
                    magic.CopyTo(header, 0);
                    inFs.ReadExactly(header.AsSpan(8));

                    int iterations = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8, 4));
                    int chunkSize = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12, 4));
                    if (iterations is < 10_000 or > 5_000_000 || chunkSize is < 1024 or > (16 * 1024 * 1024))
                    {
                        return (false, string.Empty, LocalizationService.GetText(
                            "Geçersiz FCVAULT2 başlık parametreleri.",
                            "Invalid FCVAULT2 header parameters."));
                    }

                    ReadOnlySpan<byte> salt = header.AsSpan(16, 16);
                    ReadOnlySpan<byte> baseNonce = header.AsSpan(32, 12);

                    byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(
                        passwordBytes,
                        salt,
                        iterations,
                        HashAlgorithmName.SHA256,
                        outputLength: 32);
                    Buffer.BlockCopy(derivedKey, 0, key, 0, 32);
                    CryptographicOperations.ZeroMemory(derivedKey);

                    plainBuf = new byte[chunkSize];
                    byte[] cipherBuf = new byte[chunkSize];
                    Span<byte> lenBytes = stackalloc byte[4];
                    Span<byte> tag = stackalloc byte[VaultTagLength];
                    Span<byte> chunkNonce = stackalloc byte[12];
                    Span<byte> aad = stackalloc byte[53];

                    bool sawFinalChunk = false;
                    ulong chunkIndex = 0;

                    using (var aes = new AesGcm(key, VaultTagLength))
                    using (var outFs = new FileStream(tempOutPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
                    {
                        while (inFs.Position < inFs.Length)
                        {
                            inFs.ReadExactly(lenBytes);
                            int plainLen = BinaryPrimitives.ReadInt32LittleEndian(lenBytes);
                            if (plainLen < 0 || plainLen > chunkSize)
                                throw new CryptographicException("Invalid chunk length.");

                            inFs.ReadExactly(tag);
                            if (plainLen > 0)
                                inFs.ReadExactly(cipherBuf.AsSpan(0, plainLen));

                            bool isFinalChunk = inFs.Position >= inFs.Length;
                            DeriveChunkNonce(baseNonce, chunkIndex, chunkNonce);
                            BuildChunkAad(header, chunkIndex, isFinalChunk, aad);

                            aes.Decrypt(
                                chunkNonce,
                                cipherBuf.AsSpan(0, plainLen),
                                tag,
                                plainBuf.AsSpan(0, plainLen),
                                aad);

                            if (plainLen > 0)
                                outFs.Write(plainBuf.AsSpan(0, plainLen));

                            sawFinalChunk = isFinalChunk;
                            chunkIndex++;
                        }

                        outFs.Flush(flushToDisk: true);
                    }

                    if (!sawFinalChunk || inFs.Position != inFs.Length)
                        throw new CryptographicException("Incomplete encrypted vault stream.");
                }
                else if (magic.AsSpan().SequenceEqual(VaultMagicHeaderV1))
                {
                    if (inFs.Length < VaultMagicHeaderV1.Length + 16 + 12 + VaultTagLength)
                    {
                        return (false, string.Empty, LocalizationService.GetText(
                            "Geçersiz veya bozuk .fcvault dosyası.",
                            "Invalid or corrupted .fcvault file."));
                    }

                    inFs.Position = VaultMagicHeaderV1.Length;
                    byte[] salt = new byte[16];
                    byte[] nonce = new byte[12];
                    byte[] tag = new byte[VaultTagLength];
                    inFs.ReadExactly(salt);
                    inFs.ReadExactly(nonce);
                    inFs.ReadExactly(tag);

                    int cipherLen = checked((int)(inFs.Length - inFs.Position));
                    byte[] cipherBytes = new byte[cipherLen];
                    if (cipherLen > 0)
                        inFs.ReadExactly(cipherBytes);

                    plainBuf = new byte[cipherLen];
                    byte[] derivedKey = Rfc2898DeriveBytes.Pbkdf2(
                        passwordBytes,
                        salt,
                        iterations: VaultV1Iterations,
                        HashAlgorithmName.SHA256,
                        outputLength: 32);
                    Buffer.BlockCopy(derivedKey, 0, key, 0, 32);
                    CryptographicOperations.ZeroMemory(derivedKey);

                    using (var aes = new AesGcm(key, VaultTagLength))
                    {
                        aes.Decrypt(nonce, cipherBytes, tag, plainBuf);
                    }

                    using (var outFs = new FileStream(tempOutPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
                    {
                        if (cipherLen > 0)
                            outFs.Write(plainBuf, 0, cipherLen);
                        outFs.Flush(flushToDisk: true);
                    }
                }
                else
                {
                    return (false, string.Empty, LocalizationService.GetText(
                        "Bu dosya geçerli bir FreeCleaner AES-256 (.fcvault) kasası değil.",
                        "This file is not a valid FreeCleaner AES-256 (.fcvault) vault."));
                }

                File.Move(tempOutPath, outPath, overwrite: true);

                return (true, outPath, LocalizationService.GetText(
                    $"Şifre başarıyla çözüldü ve dosya kurtarıldı: {Path.GetFileName(outPath)}",
                    $"Decrypted and restored successfully: {Path.GetFileName(outPath)}"));
            }
            catch (Exception ex) when (ex is CryptographicException or EndOfStreamException)
            {
                return (false, string.Empty, LocalizationService.GetText(
                    "Hatalı parola veya dosya bütünlüğü bozulmuş! (AES-GCM doğrulaması başarısız).",
                    "Wrong password or corrupted vault integrity! (AES-GCM authentication failed)."));
            }
            catch (Exception ex)
            {
                ErrorLogService.Log("CryptoVault:Decrypt", ex);
                return (false, string.Empty, LocalizationService.GetText(
                    $"Şifre çözme hatası: {ex.Message}",
                    $"Decryption error: {ex.Message}"));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
                CryptographicOperations.ZeroMemory(key);
                if (plainBuf is not null)
                    CryptographicOperations.ZeroMemory(plainBuf);
                try
                {
                    if (File.Exists(tempOutPath))
                        File.Delete(tempOutPath);
                }
                catch
                {
                    // Ignore cleanup error.
                }
            }
        });
    }

    // ==================== 4. WINDOWS 11 ULTIMATE TWEAKER & EXPLORER BOOSTER ====================

    public static List<WindowsTweakItem> GetWindowsTweaksState()
    {
        var list = new List<WindowsTweakItem>();

        // 1. Classic Right-Click Context Menu in Windows 11
        bool classicMenu = false;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32");
            classicMenu = k is not null;
        }
        catch { }

        list.Add(new WindowsTweakItem
        {
            TweakId = "WIN11_CLASSIC_MENU",
            Category = "Windows 11 Kabuk (Explorer)",
            Title = "Windows 11 Klasik (Tam) Sağ Tık Menüsü",
            Description = "'Diğer seçenekleri göster' adımını atlayarak doğrudan tam performanslı klasik sağ tık menüsünü açar.",
            IsEnabled = classicMenu,
            CurrentStateText = classicMenu ? "✅ Klasik Tam Menü Aktif" : "⚪ Windows 11 Kompakt Menü (Varsayılan)"
        });

        // 2. Disable Bing Web Search in Windows Search
        bool disableBing = ReadHkcuDword(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 0) == 1;
        list.Add(new WindowsTweakItem
        {
            TweakId = "DISABLE_BING_SEARCH",
            Category = "Gizlilik & Hız",
            Title = "Başlat Menüsü Bing Web Arama Sonuçlarını Kapat",
            Description = "Başlat menüsünde dosya ararken Bing internet aramalarını kapatır ve yerel aramayı hızlandırır.",
            IsEnabled = disableBing,
            CurrentStateText = disableBing ? "✅ Bing Web Araması Kapalı (Hızlı)" : "⚪ Açık (Varsayılan)"
        });

        // 3. Show Seconds in Taskbar Clock
        bool showSeconds = ReadHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSecondsInSystemClock", 0) == 1;
        list.Add(new WindowsTweakItem
        {
            TweakId = "TASKBAR_SECONDS",
            Category = "Görev Çubuğu",
            Title = "Görev Çubuğu Saatinde Saniyeleri Göster",
            Description = "Sağ alt sistem tepsisindeki Windows 11 saatinde saniye sayacını etkinleştirir.",
            IsEnabled = showSeconds,
            CurrentStateText = showSeconds ? "✅ Saniyeler Gösteriliyor" : "⚪ Kapalı (Varsayılan)"
        });

        // 4. Always Show File Extensions
        bool showExt = ReadHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", 1) == 0;
        list.Add(new WindowsTweakItem
        {
            TweakId = "SHOW_FILE_EXT",
            Category = "Güvenlik & Dosya Gezgini",
            Title = "Bilinen Dosya Uzantılarını Her Zaman Göster (.exe, .dll, .pdf)",
            Description = "Sahte ikonlu zararlı dosyaları (.pdf.exe gibi) anında fark etmek için tüm uzantıları görünür yapar.",
            IsEnabled = showExt,
            CurrentStateText = showExt ? "✅ Tüm Uzantılar Görünür" : "⚪ Gizli (Varsayılan)"
        });

        // 5. Show Hidden Files & Folders
        bool showHidden = ReadHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", 2) == 1;
        list.Add(new WindowsTweakItem
        {
            TweakId = "SHOW_HIDDEN_FILES",
            Category = "Dosya Gezgini",
            Title = "Gizli Dosya ve Klasörleri (AppData vb.) Göster",
            Description = "Dosya Gezgini'nde gizli klasörlerin görünmesini sağlar.",
            IsEnabled = showHidden,
            CurrentStateText = showHidden ? "✅ Gizli Dosyalar Görünür" : "⚪ Gizli (Varsayılan)"
        });

        // 6. Disable GameDVR Background Recording for FPS boost
        bool disableGameDvr = ReadHkcuDword(@"System\GameConfigStore", "GameDVR_Enabled", 1) == 0;
        list.Add(new WindowsTweakItem
        {
            TweakId = "DISABLE_GAMEDVR",
            Category = "Oyun & FPS Optimizasyonu",
            Title = "Xbox GameDVR Arka Plan Kaydını Kapat (FPS Artışı)",
            Description = "Oyunlarda arka plan video yakalama tamponunu kapatarak mikro-takılmaları (stutter) azaltır.",
            IsEnabled = disableGameDvr,
            CurrentStateText = disableGameDvr ? "✅ Optimize Edildi (GameDVR Kapalı)" : "⚪ Açık (Varsayılan)"
        });

        // 7. Block silent 3-4 GB Chrome/Edge On-Device AI model downloads
        bool blockBrowserOnDeviceAi = ReadHkcuDword(@"Software\Policies\Google\Chrome", "GenAILocalFoundationalModelSettings", 0) == 1;
        list.Add(new WindowsTweakItem
        {
            TweakId = "DISABLE_BROWSER_ONDEVICE_AI",
            Category = "Tarayıcı & Disk Tasarrufu",
            Title = "Chrome / Edge 4 GB Yerel AI Modeli (Gemini Nano) İndirmesini Engelle",
            Description = "Chrome ve Edge'in arka planda 3–4 GB boyutunda OptGuideOnDeviceModel (weights.bin) indirmesini kullanıcı politikası ile kalıcı engeller.",
            IsEnabled = blockBrowserOnDeviceAi,
            CurrentStateText = blockBrowserOnDeviceAi ? "✅ Model İndirme Engelli (Disk Korunuyor)" : "⚪ Varsayılan (Tarayıcı Otomatik İndirebilir)"
        });

        return list;
    }

    public static (bool Success, string Message) ToggleWindowsTweak(WindowsTweakItem item)
    {
        bool targetState = !item.IsEnabled;
        try
        {
            switch (item.TweakId)
            {
                case "WIN11_CLASSIC_MENU":
                    if (targetState)
                    {
                        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32");
                        key?.SetValue("", "", RegistryValueKind.String);
                    }
                    else
                    {
                        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}", throwOnMissingSubKey: false);
                    }
                    break;

                case "DISABLE_BING_SEARCH":
                    WriteHkcuDword(@"Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", targetState ? 1 : 0);
                    break;

                case "TASKBAR_SECONDS":
                    WriteHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "ShowSecondsInSystemClock", targetState ? 1 : 0);
                    break;

                case "SHOW_FILE_EXT":
                    WriteHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideFileExt", targetState ? 0 : 1);
                    break;

                case "SHOW_HIDDEN_FILES":
                    WriteHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "Hidden", targetState ? 1 : 2);
                    break;

                case "DISABLE_GAMEDVR":
                    WriteHkcuDword(@"System\GameConfigStore", "GameDVR_Enabled", targetState ? 0 : 1);
                    WriteHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", targetState ? 0 : 1);
                    break;

                case "DISABLE_BROWSER_ONDEVICE_AI":
                    WriteHkcuDword(@"Software\Policies\Google\Chrome", "GenAILocalFoundationalModelSettings", targetState ? 1 : 0);
                    WriteHkcuDword(@"Software\Policies\Microsoft\Edge", "GenAILocalFoundationalModelSettings", targetState ? 1 : 0);
                    break;
            }

            return (true, $"'{item.Title}' ayarı güncellendi. (Tam etki için 'Explorer Yeniden Başlat' butonunu kullanabilirsiniz).");
        }
        catch (Exception ex)
        {
            return (false, $"Ayar değiştirilemedi: {ex.Message}");
        }
    }

    public static async Task RestartWindowsExplorerAsync()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); } catch { }
                finally { p.Dispose(); }
            }

            await Task.Delay(400).ConfigureAwait(false);
            Process[] remainingExplorers = Process.GetProcessesByName("explorer");
            bool noneRunning = remainingExplorers.Length == 0;
            foreach (Process expProc in remainingExplorers)
                expProc.Dispose();

            if (noneRunning)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Explorer:Restart", ex);
        }
    }

    private static int ReadHkcuDword(string subKey, string name, int defaultVal)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(subKey);
            if (k?.GetValue(name) is int v) return v;
        }
        catch { }
        return defaultVal;
    }

    private static void WriteHkcuDword(string subKey, string name, int val)
    {
        using var k = Registry.CurrentUser.CreateSubKey(subKey);
        k?.SetValue(name, val, RegistryValueKind.DWord);
    }

    // ==================== 5. BITLOCKER AUDITOR & ISO / VHD VIRTUAL DISK MANAGER ====================

    public static async Task<List<VolumeSecurityAndMountItem>> GetVolumesAndVirtualDisksAsync()
    {
        return await Task.Run(() =>
        {
            var list = new List<VolumeSecurityAndMountItem>();
            var bitlockerByLetter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var mountedImagesByLetter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string ps = @"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Get-BitLockerVolume -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Output ""BL|$($_.MountPoint)|$($_.ProtectionStatus)|$($_.EncryptionPercentage)""
}
Get-Volume | Where-Object { $_.DriveLetter } | ForEach-Object {
    $img = ($_ | Get-Partition -ErrorAction SilentlyContinue | Get-Disk -ErrorAction SilentlyContinue)
    if ($img -and $img.BusType -eq 'File Backed Virtual') {
        Write-Output ""VHD|$($_.DriveLetter):\|Sanal Disk ($($img.FriendlyName))|$($img.Location)""
    }
}";
                ProcessStartInfo psi = SafeProcessRunner.BuildEncodedPowerShellStartInfo(ps, runAsAdmin: false, redirectOutput: true);
                ProcessRunResult runResult = SafeProcessRunner.Run(psi, TimeSpan.FromSeconds(8));
                if (runResult.Started && !runResult.TimedOut)
                {
                    string output = runResult.StandardOutput;

                    foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split('|');
                        if (parts.Length >= 4 && parts[0] == "BL")
                        {
                            string mp = parts[1].Trim();
                            if (!mp.EndsWith('\\')) mp += @"\";
                            string status = parts[2].Trim();
                            string pct = parts[3].Trim();
                            bitlockerByLetter[mp] = status.Equals("On", StringComparison.OrdinalIgnoreCase)
                                ? $"🔒 BitLocker Korumalı (%{pct} Şifreli)"
                                : $"🔓 BitLocker Kapalı (%{pct})";
                        }
                        else if (parts.Length >= 4 && parts[0] == "VHD")
                        {
                            mountedImagesByLetter[parts[1].Trim()] = parts[3].Trim();
                        }
                    }
                }
            }
            catch { }

            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;

                    string root = d.Name;
                    string label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Yerel Birim" : d.VolumeLabel;
                    bool isCdRomOrIso = d.DriveType == DriveType.CDRom;
                    bool isMountedVhd = mountedImagesByLetter.TryGetValue(root, out string? vhdPath);

                    string blStatus = bitlockerByLetter.TryGetValue(root, out var bl)
                        ? bl
                        : (isCdRomOrIso ? "💿 ISO / Optik Medya (Salt Okunur)" : "🔓 Şifresiz (Standart Birim)");

                    string backingPath = isMountedVhd && !string.IsNullOrWhiteSpace(vhdPath)
                        ? vhdPath
                        : (isCdRomOrIso ? $"Bağlı ISO Sürücüsü ({root})" : "Fiziksel Disk Bölümü");

                    list.Add(new VolumeSecurityAndMountItem
                    {
                        DriveRoot = root,
                        VolumeLabel = label,
                        FileSystemAndType = $"{d.DriveFormat} • {(isCdRomOrIso ? "ISO / Sanal Optik" : (isMountedVhd ? "Sanal VHD/VHDX" : d.DriveType.ToString()))}",
                        BitLockerStatus = blStatus,
                        CapacitySummary = $"{CleanerRule.FormatBytes(d.AvailableFreeSpace)} Boş / {CleanerRule.FormatBytes(d.TotalSize)} Toplam",
                        BackingImagePath = backingPath,
                        CanDismount = isCdRomOrIso || isMountedVhd
                    });
                }
                catch { }
            }

            return list;
        });
    }

    public static async Task<(bool Success, string Message)> MountDiskImageAsync(string isoOrVhdPath)
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(isoOrVhdPath) || !File.Exists(isoOrVhdPath))
                return (false, "Bağlanacak geçerli bir .iso, .vhd veya .vhdx dosyası seçin.");

            try
            {
                string script = SafeProcessRunner.BuildMountDiskImageScript(isoOrVhdPath);
                ProcessStartInfo psi = SafeProcessRunner.BuildEncodedPowerShellStartInfo(script);
                using var proc = Process.Start(psi);
                proc?.WaitForExit(10000);
                return proc?.ExitCode == 0
                    ? (true, $"'{Path.GetFileName(isoOrVhdPath)}' sanal sürücü olarak başarıyla bağlandı (Mount edildi)!")
                    : (false, "İmaj bağlanamadı (Dosya zaten bağlı olabilir veya Yönetici izni gerekiyor).");
            }
            catch (Exception ex)
            {
                return (false, $"Mount hatası: {ex.Message}");
            }
        });
    }

    public static async Task<(bool Success, string Message)> DismountVolumeOrImageAsync(VolumeSecurityAndMountItem item)
    {
        return await Task.Run(() =>
        {
            try
            {
                char driveLetter = !string.IsNullOrEmpty(item.DriveRoot) ? item.DriveRoot[0] : 'C';
                string ps = SafeProcessRunner.BuildDismountDiskImageScript(item.BackingImagePath, driveLetter);
                ProcessStartInfo psi = SafeProcessRunner.BuildEncodedPowerShellStartInfo(ps);
                using var proc = Process.Start(psi);
                proc?.WaitForExit(8000);
                return (true, $"{item.DriveRoot} ({item.VolumeLabel}) sanal diski / ISO imajı sistemden ayrıldı (Dismount).");
            }
            catch (Exception ex)
            {
                return (false, $"Dismount hatası: {ex.Message}");
            }
        });
    }
}
