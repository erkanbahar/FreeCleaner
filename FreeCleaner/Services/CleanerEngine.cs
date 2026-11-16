using System.Diagnostics;
using System.IO;
using FreeCleaner.Models;

namespace FreeCleaner.Services;

public class CleanerEngine
{
    public static List<CleanerRule> GetDefaultRules()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string userTemp = Path.GetTempPath();

        return
        [
            // ==================== SYSTEM ====================
            new CleanerRule
            {
                Id = "sys_user_temp",
                Name = "Kullanıcı Geçici Dosyaları (User Temp)",
                Category = "Sistem Temizleyici",
                Description = "Uygulamaların oluşturduğu geçici çalışma dosyaları (%TEMP%)",
                IconGlyph = "\uE74D",
                Group = RuleGroup.System,
                IsSelected = true,
                Targets = [new ScanTarget(userTemp)]
            },
            new CleanerRule
            {
                Id = "sys_win_temp",
                Name = "Windows Geçici Dosyaları (Windows Temp)",
                Category = "Sistem Temizleyici",
                Description = "Windows işletim sistemi geçici klasörü (C:\\Windows\\Temp)",
                IconGlyph = "\uE770",
                Group = RuleGroup.System,
                IsSelected = true,
                Targets = [new ScanTarget(Path.Combine(winDir, "Temp"))]
            },
            new CleanerRule
            {
                Id = "sys_recycle_bin",
                Name = "Geri Dönüşüm Kutusu (Recycle Bin)",
                Category = "Sistem Temizleyici",
                Description = "Silinmiş ancak diskte hâlâ yer kaplayan Geri Dönüşüm Kutusu öğeleri",
                IconGlyph = "\uE74D",
                Group = RuleGroup.System,
                IsRecycleBin = true,
                IsSelected = true,
                Risk = RiskLevel.High
            },
            new CleanerRule
            {
                Id = "sys_thumb_cache",
                Name = "Küçük Resim Önbelleği (Thumbnail Cache)",
                Category = "Windows Gezgini",
                Description = "Windows Gezgini küçük resim önbellek veritabanları (thumbcache_*.db)",
                IconGlyph = "\uEB9F",
                Group = RuleGroup.System,
                IsSelected = true,
                Targets =
                [
                    new ScanTarget(
                        Path.Combine(localAppData, "Microsoft", "Windows", "Explorer"),
                        "thumbcache_*.db",
                        Recursive: false)
                ]
            },
            new CleanerRule
            {
                Id = "sys_crash_dumps",
                Name = "Hata Dökümleri ve Raporları (Crash Dumps & WER)",
                Category = "Sistem Tanılama",
                Description = "Uygulama çökme dökümleri (CrashDumps) ve Windows Hata Raporlama (WER) kayıtları",
                IconGlyph = "\uE7BA",
                Group = RuleGroup.System,
                IsSelected = true,
                Targets =
                [
                    new ScanTarget(Path.Combine(localAppData, "CrashDumps")),
                    new ScanTarget(Path.Combine(localAppData, "Microsoft", "Windows", "WER")),
                    new ScanTarget(@"C:\ProgramData\Microsoft\Windows\WER")
                ]
            },
            new CleanerRule
            {
                Id = "sys_win_logs",
                Name = "Windows Güncelleme & Sistem Logları",
                Category = "Sistem Bakımı",
                Description = "Windows Update indirme önbelleği ve CBS/DISM günlük dosyaları",
                IconGlyph = "\uE9F9",
                Group = RuleGroup.System,
                IsSelected = true,
                Targets =
                [
                    new ScanTarget(Path.Combine(winDir, "SoftwareDistribution", "Download")),
                    new ScanTarget(Path.Combine(winDir, "Logs", "CBS"), "*.log")
                ]
            },
            new CleanerRule
            {
                Id = "sys_recent_docs",
                Name = "Son Kullanılan Belgeler Kısayolları",
                Category = "Windows Gezgini",
                Description = "Hızlı erişim ve son açılan dosya kısayol geçmişi (*.lnk)",
                IconGlyph = "\uE81C",
                Group = RuleGroup.System,
                IsSelected = false,
                Targets =
                [
                    new ScanTarget(
                        Path.Combine(appData, "Microsoft", "Windows", "Recent"),
                        "*.lnk",
                        Recursive: false)
                ]
            },
            new CleanerRule
            {
                Id = "sys_prefetch",
                Name = "Windows Prefetch Verileri",
                Category = "Sistem İleri Düzey",
                Description = "Windows uygulama ön yükleme önbelleği (Yönetici izni gerektirir)",
                IconGlyph = "\uE945",
                Group = RuleGroup.System,
                IsSelected = false,
                Targets = [new ScanTarget(Path.Combine(winDir, "Prefetch"), "*.pf", Recursive: false)]
            },

            // ==================== BROWSERS ====================
            new CleanerRule
            {
                Id = "brw_chrome_cache",
                Name = "Google Chrome - Önbellek & Code Cache",
                Category = "Google Chrome",
                Description = "Chrome web sayfası önbelleği, GPUCache ve JavaScript Code Cache dosyaları",
                IconGlyph = "\uE774",
                Group = RuleGroup.Browsers,
                IsSelected = true,
                Targets = GetChromiumCacheTargets(Path.Combine(localAppData, "Google", "Chrome", "User Data"))
            },
            new CleanerRule
            {
                Id = "brw_chrome_cookies",
                Name = "Google Chrome - Çerezler (Cookies)",
                Category = "Google Chrome",
                Description = "Sitelerdeki oturumlarınızı kapatabilir (İsteğe bağlı)",
                IconGlyph = "\uE8D7",
                Group = RuleGroup.Browsers,
                IsSelected = false,
                RequiresWarning = true,
                Risk = RiskLevel.High,
                WarningMessage = "Chrome çerezlerini silmek açık web sitesi oturumlarınızdan çıkış yapılmasına neden olur.",
                Targets = GetChromiumCookieTargets(Path.Combine(localAppData, "Google", "Chrome", "User Data"))
            },
            new CleanerRule
            {
                Id = "brw_edge_cache",
                Name = "Microsoft Edge - Önbellek & Code Cache",
                Category = "Microsoft Edge",
                Description = "Edge tarayıcı önbelleği, GPUCache ve Service Worker önbellekleri",
                IconGlyph = "\uE774",
                Group = RuleGroup.Browsers,
                IsSelected = true,
                Targets = GetChromiumCacheTargets(Path.Combine(localAppData, "Microsoft", "Edge", "User Data"))
            },
            new CleanerRule
            {
                Id = "brw_edge_cookies",
                Name = "Microsoft Edge - Çerezler (Cookies)",
                Category = "Microsoft Edge",
                Description = "Edge web oturumu çerez veritabanları (İsteğe bağlı)",
                IconGlyph = "\uE8D7",
                Group = RuleGroup.Browsers,
                IsSelected = false,
                RequiresWarning = true,
                Risk = RiskLevel.High,
                WarningMessage = "Edge çerezlerini silmek açık web sitesi oturumlarınızdan çıkış yapılmasına neden olur.",
                Targets = GetChromiumCookieTargets(Path.Combine(localAppData, "Microsoft", "Edge", "User Data"))
            },
            new CleanerRule
            {
                Id = "brw_brave_cache",
                Name = "Brave Browser - Önbellek & Code Cache",
                Category = "Brave / Opera / Vivaldi",
                Description = "Brave, Opera ve Vivaldi Chromium tabanlı tarayıcı önbellekleri",
                IconGlyph = "\uE774",
                Group = RuleGroup.Browsers,
                IsSelected = true,
                Targets =
                [
                    ..GetChromiumCacheTargets(Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data")),
                    ..GetChromiumCacheTargets(Path.Combine(localAppData, "Vivaldi", "User Data")),
                    new ScanTarget(Path.Combine(localAppData, "Opera Software", "Opera Stable", "Cache")),
                    new ScanTarget(Path.Combine(localAppData, "Opera Software", "Opera GX Stable", "Cache"))
                ]
            },
            new CleanerRule
            {
                Id = "brw_firefox_cache",
                Name = "Mozilla Firefox - Tarayıcı Önbelleği",
                Category = "Mozilla Firefox",
                Description = "Firefox profillerindeki cache2 ve startupCache geçici verileri",
                IconGlyph = "\uE774",
                Group = RuleGroup.Browsers,
                IsSelected = true,
                Targets = GetFirefoxCacheTargets(Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles"))
            },

            // ==================== DEVELOPER & APPS ====================
            new CleanerRule
            {
                Id = "app_vscode_cache",
                Name = "VS Code & Cursor Önbellekleri",
                Category = "Geliştirici Araçları",
                Description = "VS Code ve Cursor IDE CachedData, GPUCache ve log dosyaları",
                IconGlyph = "\uE943",
                Group = RuleGroup.DeveloperAndApps,
                IsSelected = true,
                Targets =
                [
                    new ScanTarget(Path.Combine(appData, "Code", "Cache")),
                    new ScanTarget(Path.Combine(appData, "Code", "CachedData")),
                    new ScanTarget(Path.Combine(appData, "Code", "GPUCache")),
                    new ScanTarget(Path.Combine(appData, "Code", "logs")),
                    new ScanTarget(Path.Combine(appData, "Cursor", "Cache")),
                    new ScanTarget(Path.Combine(appData, "Cursor", "GPUCache")),
                    new ScanTarget(Path.Combine(appData, "Cursor", "logs"))
                ]
            },
            new CleanerRule
            {
                Id = "app_comm_cache",
                Name = "Discord, Slack & Microsoft Teams Önbelleği",
                Category = "Uygulama Önbellekleri",
                Description = "İletişim uygulamalarının medya, GPU ve web önbellekleri",
                IconGlyph = "\uE8BD",
                Group = RuleGroup.DeveloperAndApps,
                IsSelected = true,
                Targets =
                [
                    new ScanTarget(Path.Combine(appData, "discord", "Cache")),
                    new ScanTarget(Path.Combine(appData, "discord", "Code Cache")),
                    new ScanTarget(Path.Combine(appData, "discord", "GPUCache")),
                    new ScanTarget(Path.Combine(appData, "Slack", "Cache")),
                    new ScanTarget(Path.Combine(appData, "Slack", "Code Cache")),
                    new ScanTarget(Path.Combine(appData, "Slack", "GPUCache")),
                    new ScanTarget(Path.Combine(localAppData, "Packages", "MSTeams_8wekyb3d8bbwe", "LocalCache", "Microsoft", "MSTeams", "EBWebView", "Default", "Cache"))
                ]
            },
            new CleanerRule
            {
                Id = "app_dotnet_npm_cache",
                Name = "NuGet & NPM HTTP Geçici Önbelleği",
                Category = "Geliştirici Araçları",
                Description = "NuGet v3-cache ve npm _cacache indirilen paket önbellekleri",
                IconGlyph = "\uE7B8",
                Group = RuleGroup.DeveloperAndApps,
                IsSelected = false,
                Targets =
                [
                    new ScanTarget(Path.Combine(localAppData, "NuGet", "v3-cache")),
                    new ScanTarget(Path.Combine(localAppData, "npm-cache", "_cacache"))
                ]
            },
            new CleanerRule
            {
                Id = "app_vs_debug_retail",
                Name = "Visual Studio Projeleri (Debug & Retail / Release)",
                Category = "Visual Studio & .NET",
                Description = "C:\\CURSOR ve Repos içindeki Visual Studio projelerinin Debug, Retail, Release, bin ve obj klasörleri",
                IconGlyph = "\uE943",
                Group = RuleGroup.DeveloperAndApps,
                IsSelected = false,
                RequiresWarning = true,
                Risk = RiskLevel.High,
                WarningMessage = "Seçili klasörlerdeki Visual Studio projelerinin Debug ve Retail/Release derleme çıktıları temizlenecektir.",
                Targets = GetVisualStudioBuildFolderTargets()
            },
            new CleanerRule
            {
                Id = "app_directx_shader",
                Name = "DirectX & GPU Shader Önbelleği",
                Category = "Grafik & Oyun",
                Description = "Direct3D (D3DSCache) derlenmiş gölgelendirici önbelleği",
                IconGlyph = "\uE7FC",
                Group = RuleGroup.DeveloperAndApps,
                IsSelected = true,
                Targets =
                [
                    new ScanTarget(Path.Combine(localAppData, "D3DSCache"))
                ]
            }
        ];
    }

    private static List<ScanTarget> GetVisualStudioBuildFolderTargets()
    {
        var targets = new List<ScanTarget>();
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidateRoots =
        [
            @"C:\CURSOR",
            Path.Combine(userProfile, "source", "repos"),
            Path.Combine(userProfile, "Repos"),
            Path.Combine(userProfile, "Projects")
        ];

        foreach (string root in candidateRoots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (string subDir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories))
                {
                    if (subDir.Contains("FreeCleaner", StringComparison.OrdinalIgnoreCase) ||
                        subDir.Contains("node_modules", StringComparison.OrdinalIgnoreCase) ||
                        subDir.Contains(".git", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string folderName = Path.GetFileName(subDir);
                    if (folderName.Equals("Debug", StringComparison.OrdinalIgnoreCase) ||
                        folderName.Equals("Retail", StringComparison.OrdinalIgnoreCase) ||
                        folderName.Equals("Release", StringComparison.OrdinalIgnoreCase))
                    {
                        targets.Add(new ScanTarget(subDir));
                    }
                }
            }
            catch
            {
                // Ignore inaccessible subdirectories
            }
        }
        return targets;
    }

    private static List<ScanTarget> GetChromiumCacheTargets(string userDataDir)
    {
        List<ScanTarget> targets = [];
        string[] profileNames = ["Default", "Profile 1", "Profile 2", "Profile 3", "Profile 4", "Profile 5"];

        // Also dynamically discover existing Profile* folders if directory exists
        if (Directory.Exists(userDataDir))
        {
            try
            {
                var foundProfiles = Directory.GetDirectories(userDataDir)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrEmpty(name) &&
                                   (name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                                    name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase) ||
                                    name.Equals("Guest Profile", StringComparison.OrdinalIgnoreCase)))
                    .Cast<string>()
                    .Distinct()
                    .ToArray();

                if (foundProfiles.Length > 0)
                {
                    profileNames = foundProfiles;
                }
            }
            catch
            {
                // Keep default profile names
            }
        }

        foreach (string profile in profileNames)
        {
            string profilePath = Path.Combine(userDataDir, profile);
            targets.Add(new ScanTarget(Path.Combine(profilePath, "Cache", "Cache_Data")));
            targets.Add(new ScanTarget(Path.Combine(profilePath, "Cache")));
            targets.Add(new ScanTarget(Path.Combine(profilePath, "Code Cache")));
            targets.Add(new ScanTarget(Path.Combine(profilePath, "GPUCache")));
            targets.Add(new ScanTarget(Path.Combine(profilePath, "Service Worker", "CacheStorage")));
            targets.Add(new ScanTarget(Path.Combine(profilePath, "Service Worker", "ScriptCache")));
        }

        targets.Add(new ScanTarget(Path.Combine(userDataDir, "ShaderCache")));
        targets.Add(new ScanTarget(Path.Combine(userDataDir, "GrShaderCache")));
        return targets;
    }

    private static List<ScanTarget> GetChromiumCookieTargets(string userDataDir)
    {
        List<ScanTarget> targets = [];
        string[] profileNames = ["Default", "Profile 1", "Profile 2", "Profile 3"];
        foreach (string profile in profileNames)
        {
            string networkPath = Path.Combine(userDataDir, profile, "Network");
            targets.Add(new ScanTarget(networkPath, "Cookies*", Recursive: false));
        }
        return targets;
    }

    private static List<ScanTarget> GetFirefoxCacheTargets(string profilesRoot)
    {
        List<ScanTarget> targets = [];
        if (!Directory.Exists(profilesRoot))
        {
            targets.Add(new ScanTarget(profilesRoot));
            return targets;
        }

        try
        {
            foreach (string profileDir in Directory.GetDirectories(profilesRoot))
            {
                targets.Add(new ScanTarget(Path.Combine(profileDir, "cache2")));
                targets.Add(new ScanTarget(Path.Combine(profileDir, "startupCache")));
                targets.Add(new ScanTarget(Path.Combine(profileDir, "jumpListCache")));
            }
        }
        catch
        {
            // Ignore access errors
        }
        return targets;
    }

    public async Task<List<ScannedFileItem>> AnalyzeAsync(
        IEnumerable<CleanerRule> rules,
        IReadOnlyList<string>? excludedPaths = null,
        IProgress<(int Percent, string CurrentRuleName, long TotalFoundBytes, int TotalFoundCount)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var activeRules = rules.Where(r => r.IsSelected).ToList();
        var allDiscoveredItems = new List<ScannedFileItem>();
        var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exclusions = excludedPaths ?? [];

        foreach (var rule in rules)
        {
            rule.DetectedSizeBytes = 0;
            rule.DetectedFileCount = 0;
            rule.StatusText = rule.IsSelected ? "Bekliyor..." : "Atlandı";
        }

        if (activeRules.Count == 0)
            return allDiscoveredItems;

        long runningTotalBytes = 0;
        int runningTotalCount = 0;

        await Task.Run(() =>
        {
            for (int i = 0; i < activeRules.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rule = activeRules[i];
                rule.StatusText = "Taranıyor...";

                int pct = (int)((i / (double)activeRules.Count) * 100);
                progress?.Report((pct, rule.Name, runningTotalBytes, runningTotalCount));

                long ruleBytes = 0;
                int ruleCount = 0;

                if (rule.IsRecycleBin)
                {
                    var (rbSize, rbCount) = RecycleBinHelper.QueryRecycleBin();
                    if (rbCount > 0 || rbSize > 0)
                    {
                        var rbItem = new ScannedFileItem
                        {
                            RuleId = rule.Id,
                            RuleName = rule.Name,
                            Category = rule.Category,
                            FullPath = $"Geri Dönüşüm Kutusu ({rbCount:N0} öğe)",
                            FileName = $"Geri Dönüşüm Kutusu ({rbCount:N0} öğe)",
                            SizeBytes = rbSize,
                            LastModified = DateTime.Now,
                            IsRecycleBinItem = true,
                            Risk = rule.Risk,
                            Status = "Analiz Edildi"
                        };
                        allDiscoveredItems.Add(rbItem);
                        ruleBytes += rbSize;
                        ruleCount += Math.Max(1, rbCount);
                        runningTotalBytes += rbSize;
                        runningTotalCount += Math.Max(1, rbCount);
                    }
                }
                else
                {
                    foreach (var target in rule.Targets)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (string.IsNullOrWhiteSpace(target.BasePath) || !Directory.Exists(target.BasePath))
                            continue;

                        foreach (var file in EnumerateFilesSafe(target.BasePath, target.SearchPattern, target.Recursive, cancellationToken))
                        {
                            if (!visitedPaths.Add(file.FullName))
                                continue;

                            if (ExclusionAndSettingsService.IsPathExcluded(file.FullName, exclusions))
                                continue;

                            long fileLen = 0;
                            DateTime lastWrite = DateTime.Now;
                            try
                            {
                                fileLen = file.Length;
                                lastWrite = file.LastWriteTime;
                            }
                            catch
                            {
                                continue;
                            }

                            var item = new ScannedFileItem
                            {
                                RuleId = rule.Id,
                                RuleName = rule.Name,
                                Category = rule.Category,
                                FullPath = file.FullName,
                                FileName = file.Name,
                                SizeBytes = fileLen,
                                LastModified = lastWrite,
                                Risk = rule.Risk,
                                Status = "Analiz Edildi"
                            };

                            allDiscoveredItems.Add(item);
                            ruleBytes += fileLen;
                            ruleCount++;
                            runningTotalBytes += fileLen;
                            runningTotalCount++;
                        }
                    }
                }

                rule.DetectedSizeBytes = ruleBytes;
                rule.DetectedFileCount = ruleCount;
                rule.StatusText = ruleCount > 0 ? $"{ruleCount:N0} dosya ({CleanerRule.FormatBytes(ruleBytes)})" : "Temiz";

                int donePct = (int)(((i + 1) / (double)activeRules.Count) * 100);
                progress?.Report((donePct, rule.Name, runningTotalBytes, runningTotalCount));
            }
        }, cancellationToken);

        return allDiscoveredItems
            .OrderByDescending(x => x.SizeBytes)
            .ToList();
    }

    public async Task<CleanSummaryResult> CleanAsync(
        IEnumerable<ScannedFileItem> itemsToClean,
        IEnumerable<CleanerRule> rules,
        bool moveToRecycleBinInsteadOfPermanentDelete,
        IProgress<(int Percent, string CurrentFile, long FreedBytes, int DeletedCount)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var summary = new CleanSummaryResult();
        var selectedItems = itemsToClean.Where(x => x.IsSelected).ToList();

        if (selectedItems.Count == 0)
        {
            stopwatch.Stop();
            summary.Elapsed = stopwatch.Elapsed;
            return summary;
        }

        var ruleLookup = rules.ToDictionary(r => r.Id, r => r);
        var freedByRule = new Dictionary<string, (long Bytes, int Count)>();

        await Task.Run(() =>
        {
            int total = selectedItems.Count;
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = selectedItems[i];

                bool deleted = false;
                if (item.IsRecycleBinItem)
                {
                    deleted = RecycleBinHelper.EmptyRecycleBin();
                    if (deleted)
                    {
                        item.Status = "Boşaltıldı";
                        summary.Logs.Add($"[BAŞARILI] Geri Dönüşüm Kutusu boşaltıldı ({item.FormattedSize})");
                    }
                    else
                    {
                        item.Status = "Atlandı / Hata";
                        summary.Logs.Add("[ATLANDI] Geri Dönüşüm Kutusu boşaltılamadı veya zaten boş.");
                    }
                }
                else
                {
                    try
                    {
                        if (!File.Exists(item.FullPath))
                        {
                            deleted = true;
                            item.Status = "Zaten Silinmiş";
                        }
                        else if (moveToRecycleBinInsteadOfPermanentDelete)
                        {
                            deleted = RecycleBinHelper.MoveFileToRecycleBin(item.FullPath);
                            item.Status = deleted ? "Geri Dönüşüm'e Taşındı" : "Kilitli / Kullanımda";
                        }
                        else
                        {
                            // Clear ReadOnly attribute if present on temp files
                            var attrs = File.GetAttributes(item.FullPath);
                            if ((attrs & FileAttributes.ReadOnly) != 0)
                            {
                                File.SetAttributes(item.FullPath, attrs & ~FileAttributes.ReadOnly);
                            }
                            File.Delete(item.FullPath);
                            deleted = true;
                            item.Status = "Silindi";
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        deleted = false;
                        item.Status = "Kilitli (Kullanımda)";
                    }
                    catch
                    {
                        deleted = false;
                        item.Status = "Atlandı";
                    }
                }

                if (deleted)
                {
                    summary.DeletedFilesCount++;
                    summary.FreedBytes += item.SizeBytes;

                    if (!freedByRule.TryGetValue(item.RuleId, out var current))
                        current = (0, 0);
                    freedByRule[item.RuleId] = (current.Bytes + item.SizeBytes, current.Count + 1);
                }
                else
                {
                    summary.SkippedFilesCount++;
                }

                if (i % 15 == 0 || i == total - 1)
                {
                    int pct = (int)(((i + 1) / (double)total) * 100);
                    progress?.Report((pct, item.FileName, summary.FreedBytes, summary.DeletedFilesCount));
                }
            }

            // Try cleaning up empty subdirectories in Temp folders safely
            foreach (var rule in rules.Where(r => r.IsSelected && !r.IsRecycleBin))
            {
                foreach (var target in rule.Targets.Where(t => t.Recursive && t.SearchPattern == "*"))
                {
                    DeleteEmptySubdirectoriesSafe(target.BasePath);
                }
            }
        }, cancellationToken);

        foreach (var (ruleId, (freedBytes, deletedCount)) in freedByRule)
        {
            if (ruleLookup.TryGetValue(ruleId, out var rule))
            {
                rule.DetectedSizeBytes = Math.Max(0, rule.DetectedSizeBytes - freedBytes);
                rule.DetectedFileCount = Math.Max(0, rule.DetectedFileCount - deletedCount);
                rule.StatusText = $"Temizlendi ({CleanerRule.FormatBytes(freedBytes)})";
            }
        }

        stopwatch.Stop();
        summary.Elapsed = stopwatch.Elapsed;
        return summary;
    }

    private static IEnumerable<FileInfo> EnumerateFilesSafe(
        string rootPath,
        string searchPattern,
        bool recursive,
        CancellationToken cancellationToken)
    {
        var pendingDirs = new Stack<string>();
        pendingDirs.Push(rootPath);

        // Protect against deleting files created in the last 2 minutes inside active temp folders
        DateTime safeThresholdUtc = DateTime.UtcNow.AddMinutes(-1);

        while (pendingDirs.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string currentDir = pendingDirs.Pop();

            FileInfo[]? files = null;
            try
            {
                var dirInfo = new DirectoryInfo(currentDir);
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    continue; // Avoid following symlinks/junction loops

                files = dirInfo.GetFiles(searchPattern, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                // Ignore UnauthorizedAccessException / PathTooLongException / IOException
            }

            if (files is not null)
            {
                foreach (var file in files)
                {
                    // Skip files actively modified within the last 60 seconds in user temp to avoid breaking running installers
                    if (file.Name.StartsWith("FreeCleaner", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (currentDir.Contains("Temp", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            if (file.LastWriteTimeUtc > safeThresholdUtc)
                                continue;
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    yield return file;
                }
            }

            if (recursive)
            {
                try
                {
                    foreach (string subDir in Directory.GetDirectories(currentDir))
                    {
                        pendingDirs.Push(subDir);
                    }
                }
                catch
                {
                    // Ignore inaccessible subdirectories
                }
            }
        }
    }

    private static void DeleteEmptySubdirectoriesSafe(string rootDir)
    {
        if (string.IsNullOrWhiteSpace(rootDir) || !Directory.Exists(rootDir))
            return;

        try
        {
            foreach (string subDir in Directory.GetDirectories(rootDir))
            {
                try
                {
                    if (Directory.GetFiles(subDir, "*", SearchOption.AllDirectories).Length == 0)
                    {
                        Directory.Delete(subDir, recursive: true);
                    }
                }
                catch
                {
                    // Ignore locked directories
                }
            }
        }
        catch
        {
            // Ignore root access errors
        }
    }
}
