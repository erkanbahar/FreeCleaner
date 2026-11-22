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
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{ps.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(9000);

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

    private static readonly byte[] VaultMagicHeader = "FCVAULT1"u8.ToArray();

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
                string md5 = Convert.ToHexString(MD5.HashData(fs1));
                results.Add(new FileHashAndVaultResultItem
                {
                    AlgorithmOrOperation = "MD5 (128-bit)",
                    FileName = fileName,
                    HashOrOutputValue = md5,
                    VerificationStatus = MatchStatus(md5)
                });

                fs1.Position = 0;
                string sha1 = Convert.ToHexString(SHA1.HashData(fs1));
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

    public static async Task<(bool Success, string OutputFilePath, string Message)> EncryptFileAes256GcmAsync(string inputFilePath, string password)
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(inputFilePath) || !File.Exists(inputFilePath))
                return (false, string.Empty, "Şifrelenecek geçerli bir dosya seçin.");
            if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
                return (false, string.Empty, "Lütfen en az 4 karakterli bir şifreleme parolası girin.");

            try
            {
                byte[] plainBytes = File.ReadAllBytes(inputFilePath);
                byte[] salt = RandomNumberGenerator.GetBytes(16);
                byte[] nonce = RandomNumberGenerator.GetBytes(12);
                byte[] tag = new byte[16];
                byte[] cipherBytes = new byte[plainBytes.Length];

                byte[] key = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password),
                    salt,
                    iterations: 100_000,
                    HashAlgorithmName.SHA256,
                    outputLength: 32);

                using (var aes = new AesGcm(key, 16))
                {
                    aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
                }

                string outPath = inputFilePath + ".fcvault";
                using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(VaultMagicHeader);
                    fs.Write(salt);
                    fs.Write(nonce);
                    fs.Write(tag);
                    fs.Write(cipherBytes);
                }

                return (true, outPath, $"Dosya AES-256-GCM (PBKDF2 100.000 İterasyon) ile şifrelendi: {Path.GetFileName(outPath)}");
            }
            catch (Exception ex)
            {
                return (false, string.Empty, $"Şifreleme hatası: {ex.Message}");
            }
        });
    }

    public static async Task<(bool Success, string OutputFilePath, string Message)> DecryptFileAes256GcmAsync(string vaultFilePath, string password)
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(vaultFilePath) || !File.Exists(vaultFilePath))
                return (false, string.Empty, "Şifresi çözülecek .fcvault dosyasını seçin.");
            if (string.IsNullOrWhiteSpace(password))
                return (false, string.Empty, "Lütfen şifre çözme parolasını girin.");

            try
            {
                byte[] raw = File.ReadAllBytes(vaultFilePath);
                int headerLen = VaultMagicHeader.Length;
                if (raw.Length < headerLen + 16 + 12 + 16)
                    return (false, string.Empty, "Geçersiz veya bozuk .fcvault dosyası.");

                for (int i = 0; i < headerLen; i++)
                {
                    if (raw[i] != VaultMagicHeader[i])
                        return (false, string.Empty, "Bu dosya geçerli bir FreeCleaner AES-256 (.fcvault) kasası değil.");
                }

                byte[] salt = raw.AsSpan(headerLen, 16).ToArray();
                byte[] nonce = raw.AsSpan(headerLen + 16, 12).ToArray();
                byte[] tag = raw.AsSpan(headerLen + 28, 16).ToArray();
                byte[] cipherBytes = raw.AsSpan(headerLen + 44).ToArray();
                byte[] plainBytes = new byte[cipherBytes.Length];

                byte[] key = Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(password),
                    salt,
                    iterations: 100_000,
                    HashAlgorithmName.SHA256,
                    outputLength: 32);

                using (var aes = new AesGcm(key, 16))
                {
                    aes.Decrypt(nonce, cipherBytes, tag, plainBytes);
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

                File.WriteAllBytes(outPath, plainBytes);
                return (true, outPath, $"Şifre başarıyla çözüldü ve dosya kurtarıldı: {Path.GetFileName(outPath)}");
            }
            catch (CryptographicException)
            {
                return (false, string.Empty, "Hatalı parola veya dosya bütünlüğü bozulmuş! (AES-GCM doğrulaması başarısız).");
            }
            catch (Exception ex)
            {
                return (false, string.Empty, $"Şifre çözme hatası: {ex.Message}");
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

    public static void RestartWindowsExplorer()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); } catch { }
                finally { p.Dispose(); }
            }

            Thread.Sleep(400);
            if (Process.GetProcessesByName("explorer").Length == 0)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                });
            }
        }
        catch { }
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
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{ps.Replace("\"", "\\\"")}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc is not null)
                {
                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(6000);

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
