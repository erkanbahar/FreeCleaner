using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FreeCleaner.Models;
using FreeCleaner.Services;

namespace FreeCleaner.ViewModels;

public partial class MainViewModel
{
    // ==================== 17. FLAGSHIP DASHBOARD, FAVORITES, PALETTE, LIVE TELEMETRY & THEMES ====================

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (_selectedTheme != value && !string.IsNullOrWhiteSpace(value))
            {
                _selectedTheme = value;
                ThemeManagerService.ApplyTheme(_selectedTheme);
                OnPropertyChanged();
                SaveCurrentSettings();
                StatusMessage = LocalizationService.GetText(
                    $"Tema değiştirildi: {_selectedTheme}",
                    $"Theme switched to: {_selectedTheme}",
                    _selectedLanguage);
                ShowToast("FreeCleaner Pro", StatusMessage, ToastSeverity.Info, durationSeconds: 4);
                AddLog($"[TEMA] Uygulama görünümü '{_selectedTheme}' olarak güncellendi.");
            }
        }
    }

    public int HealthScore
    {
        get => _healthScore;
        set { if (_healthScore != value) { _healthScore = value; OnPropertyChanged(); } }
    }

    public string HealthGradeBadge
    {
        get => _healthGradeBadge;
        set { if (_healthGradeBadge != value) { _healthGradeBadge = value; OnPropertyChanged(); } }
    }

    public string HealthAdviceText
    {
        get => _healthAdviceText;
        set { if (_healthAdviceText != value) { _healthAdviceText = value; OnPropertyChanged(); } }
    }

    public double LiveCpuPercent
    {
        get => _liveCpuPercent;
        set { _liveCpuPercent = value; OnPropertyChanged(); }
    }

    public double LiveRamPercent
    {
        get => _liveRamPercent;
        set { _liveRamPercent = value; OnPropertyChanged(); }
    }

    public string LiveNetDownText
    {
        get => _liveNetDownText;
        set { _liveNetDownText = value; OnPropertyChanged(); }
    }

    public string LiveNetUpText
    {
        get => _liveNetUpText;
        set { _liveNetUpText = value; OnPropertyChanged(); }
    }

    public PointCollection CpuSparklinePoints
    {
        get => _cpuSparklinePoints;
        set { _cpuSparklinePoints = value; OnPropertyChanged(); }
    }

    public PointCollection RamSparklinePoints
    {
        get => _ramSparklinePoints;
        set { _ramSparklinePoints = value; OnPropertyChanged(); }
    }

    public PointCollection NetDownSparklinePoints
    {
        get => _netDownSparklinePoints;
        set { _netDownSparklinePoints = value; OnPropertyChanged(); }
    }

    public PointCollection NetUpSparklinePoints
    {
        get => _netUpSparklinePoints;
        set { _netUpSparklinePoints = value; OnPropertyChanged(); }
    }

    public bool IsCurrentSectionFavorite => _favoriteSectionKeys.Contains(ActiveSection, StringComparer.OrdinalIgnoreCase);

    public string FavoriteButtonText => LocalizationService.NormalizeLanguageCode(_selectedLanguage) == "en-US"
        ? (IsCurrentSectionFavorite ? "★ Pinned" : "☆ Pin Favorite")
        : (IsCurrentSectionFavorite ? "★ Favorilerde" : "☆ Favoriye Ekle");

    public void ExecuteCommandPaletteShortcut(NavShortcutItem item)
    {
        switch (item.SectionKey)
        {
            case "CMD:HealthCheck":
                ActiveSection = "Dashboard";
                if (RunOneClickHealthCheckCommand.CanExecute(null))
                    RunOneClickHealthCheckCommand.Execute(null);
                break;
            case "CMD:Analyze":
                ActiveSection = "All";
                if (AnalyzeCommand.CanExecute(null))
                    AnalyzeCommand.Execute(null);
                break;
            case "CMD:OptimizeRam":
                if (OptimizeRamCommand.CanExecute(null))
                    OptimizeRamCommand.Execute(null);
                break;
            case "CMD:FlushDns":
                if (FlushDnsCommand.CanExecute(null))
                    FlushDnsCommand.Execute(null);
                break;
            case "CMD:CheckUpdate":
                ActiveSection = "Settings";
                if (CheckForUpdatesCommand.CanExecute(null))
                    CheckForUpdatesCommand.Execute(null);
                break;
            case "CMD:ProfileSafe":
                ActiveSection = "All";
                ExecuteApplyCleaningProfile(CleaningProfileKind.SafeDaily);
                break;
            case "CMD:ProfileDev":
                ActiveSection = "All";
                ExecuteApplyCleaningProfile(CleaningProfileKind.DeveloperDeep);
                break;
            case "CMD:ProfileGamer":
                ActiveSection = "All";
                ExecuteApplyCleaningProfile(CleaningProfileKind.GamerBoost);
                break;
            case "CMD:ProfilePrivacy":
                ActiveSection = "All";
                ExecuteApplyCleaningProfile(CleaningProfileKind.MaxPrivacy);
                break;
            case "CMD:ExportSettings":
                ExecuteExportSettingsConfig();
                break;
            case "CMD:ExportSupportBundle":
                ExecuteExportSupportBundle();
                break;
            case "CMD:OrganizeDownloads":
                ActiveSection = "Downloads";
                SelectOrganizerPreset("Downloads");
                break;
            default:
                if (item.SectionKey.StartsWith("CMD:CustomProfile:", StringComparison.Ordinal))
                {
                    string profName = item.SectionKey["CMD:CustomProfile:".Length..];
                    CustomCleaningProfile? match = CustomProfiles.FirstOrDefault(p =>
                        p.Name.Equals(profName, StringComparison.OrdinalIgnoreCase));
                    if (match is not null)
                    {
                        ActiveSection = "All";
                        ExecuteApplyCustomProfile(match);
                    }
                    break;
                }

                ActiveSection = item.SectionKey;
                break;
        }
    }

    public bool HasCompletedOnboarding
    {
        get => _hasCompletedOnboarding;
        set
        {
            if (_hasCompletedOnboarding != value)
            {
                _hasCompletedOnboarding = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowOnboardingOverlay));
            }
        }
    }

    public bool ShowOnboardingOverlay => !_hasCompletedOnboarding;

    public string NewCustomProfileName
    {
        get => _newCustomProfileName;
        set
        {
            string trimmed = value ?? string.Empty;
            if (_newCustomProfileName != trimmed)
            {
                _newCustomProfileName = trimmed;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public CustomCleaningProfile? SelectedCustomProfile
    {
        get => _selectedCustomProfile;
        set
        {
            if (!ReferenceEquals(_selectedCustomProfile, value))
            {
                _selectedCustomProfile = value;
                OnPropertyChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public void SaveWindowBounds(WindowBoundsState bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        _windowLeft = bounds.Left;
        _windowTop = bounds.Top;
        _windowWidth = bounds.Width;
        _windowHeight = bounds.Height;
        _windowMaximized = bounds.IsMaximized;
        SaveCurrentSettings();
    }

    public void RefreshActiveSection()
    {
        if (IsBusy)
            return;

        ICommand? cmd = ActiveSection switch
        {
            "Dashboard" => RunOneClickHealthCheckCommand,
            "All" or "System" or "Browsers" or "Apps" => AnalyzeCommand,
            "DiskTreemap" => AnalyzeTreemapCommand,
            "DeepSpaceRecovery" => ScanDeepSpaceCommand,
            "SimilarPhotos" => ScanSimilarPhotosCommand,
            "ExifScrubber" => ScanPhotosExifCommand,
            "Startup" => RefreshStartupCommand,
            "Registry" => ScanRegistryCommand,
            "Uninstaller" => RefreshInstalledAppsCommand,
            "Booster" => RefreshTopProcessesCommand,
            "Privacy" => ScanPrivacyTracesCommand,
            "Network" => RefreshConnectionsCommand,
            "WingetUpdater" => ScanWingetUpgradesCommand,
            "PackageCaches" => ScanPackageCachesCommand,
            "AiModelCleaner" => ScanAiModelCachesCommand,
            "Bloatware" => ScanAppxBloatwareCommand,
            "Downloads" => ScanOldInstallersCommand,
            "Services" => RefreshServicesCommand,
            "PortHunter" => ScanListeningPortsCommand,
            "GitRepoCompressor" => ScanGitReposCommand,
            "DockerWsl" => ScanDockerAndWslCommand,
            "DefenderAuditor" => ScanDefenderAuditCommand,
            "BootPerformance" => ScanBootPerformanceCommand,
            "MonitorGpu" => ScanMonitorsAndGpuCommand,
            _ => null
        };

        if (cmd is not null && cmd.CanExecute(null))
            cmd.Execute(null);
        else
        {
            RefreshSystemMetrics();
            RefreshStorageForecast();
        }
    }

    private void ExecuteSaveCustomProfile()
    {
        string name = NewCustomProfileName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        List<string> selectedIds = AllRules.Where(r => r.IsSelected).Select(r => r.Id).ToList();
        if (selectedIds.Count == 0)
        {
            StatusMessage = "Özel profil kaydetmek için en az 1 temizlik kuralı seçin.";
            return;
        }

        int existingIdx = -1;
        for (int i = 0; i < CustomProfiles.Count; i++)
        {
            if (CustomProfiles[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                existingIdx = i;
                break;
            }
        }

        var profile = new CustomCleaningProfile { Name = name, RuleIds = selectedIds };
        if (existingIdx >= 0)
            CustomProfiles[existingIdx] = profile;
        else
            CustomProfiles.Add(profile);

        SelectedCustomProfile = profile;
        NewCustomProfileName = string.Empty;
        PopulateModuleCatalog();
        SaveCurrentSettings();

        StatusMessage = LocalizationService.GetText(
            $"⭐ Özel profil kaydedildi: '{name}' ({selectedIds.Count} kural).",
            $"⭐ Saved custom profile '{name}' ({selectedIds.Count} rules).",
            _selectedLanguage);
        ShowToast("FreeCleaner Pro", StatusMessage, ToastSeverity.Success);
        AddLog($"[ÖZEL PROFİL] '{name}' ({selectedIds.Count} kural) kaydedildi.");
    }

    private void ExecuteApplySelectedCustomProfile()
    {
        if (SelectedCustomProfile is not null)
            ExecuteApplyCustomProfile(SelectedCustomProfile);
    }

    private void ExecuteApplyCustomProfile(CustomCleaningProfile profile)
    {
        _suppressRuleWarningPrompt = true;
        try
        {
            var (_, desc) = ShellIntegrationAndProfilesService.ApplyCustomProfileToRules(AllRules, profile);
            var selectedIds = AllRules
                .Where(r => r.IsSelected)
                .Select(r => r.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var file in DiscoveredFiles)
                file.IsSelected = selectedIds.Contains(file.RuleId);

            RecalculateSelectedTotals();
            SaveCurrentSettings();
            StatusMessage = desc;
            AddLog($"[ÖZEL PROFİL] {desc}");
        }
        finally
        {
            _suppressRuleWarningPrompt = false;
        }
    }

    private void ExecuteDeleteSelectedCustomProfile()
    {
        if (SelectedCustomProfile is null)
            return;

        string removedName = SelectedCustomProfile.Name;
        CustomProfiles.Remove(SelectedCustomProfile);
        SelectedCustomProfile = CustomProfiles.FirstOrDefault();
        PopulateModuleCatalog();
        SaveCurrentSettings();

        StatusMessage = $"Özel profil silindi: '{removedName}'.";
        AddLog($"[ÖZEL PROFİL] '{removedName}' silindi.");
    }

    private void ToggleCurrentSectionFavorite()
    {
        int existingIdx = _favoriteSectionKeys.FindIndex(k => k.Equals(ActiveSection, StringComparison.OrdinalIgnoreCase));
        if (existingIdx >= 0)
        {
            _favoriteSectionKeys.RemoveAt(existingIdx);
            StatusMessage = $"'{SectionTitle}' favorilerden çıkarıldı.";
        }
        else
        {
            _favoriteSectionKeys.Add(ActiveSection);
            StatusMessage = $"'{SectionTitle}' favorilere sabitlendi!";
        }

        RebuildFavoriteNavItems();
        OnPropertyChanged(nameof(IsCurrentSectionFavorite));
        OnPropertyChanged(nameof(FavoriteButtonText));
        SaveCurrentSettings();
    }

    private void RebuildFavoriteNavItems()
    {
        FavoriteNavItems.Clear();
        foreach (string key in _favoriteSectionKeys)
        {
            var match = AllModuleCatalog.FirstOrDefault(m => m.SectionKey.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                FavoriteNavItems.Add(match);
        }
    }

    private void UpdateHealthScoreSummary(long junkBytes, int startupCount)
    {
        RefreshSystemMetrics();
        var (score, badge, advice) = FlagshipTelemetryAndThemeService.ComputeHealthScore(SystemMetrics, junkBytes, startupCount);
        HealthScore = score;
        HealthGradeBadge = badge;
        HealthAdviceText = advice;
    }

    public async Task RunOneClickHealthCheckAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        ActiveSection = "Dashboard";
        HealthCheckResults.Clear();
        ProgressPercent = 10;
        StatusMessage = "Tek Tıkla 100 Puanlık PC Sağlık Kontrolü başlatıldı: Sistem analiz ediliyor...";
        AddLog("[SAĞLIK KONTROLÜ] Kapsamlı sistem taraması ve hızlı optimizasyon başlatıldı.");

        try
        {
            // 1. System Disk & RAM check
            RefreshSystemMetrics();
            HealthCheckResults.Add(new HealthCheckStepItem
            {
                Category = "Depolama",
                CheckName = $"Sistem Diski ({SystemMetrics.DriveName})",
                StatusBadge = SystemMetrics.DriveUsedPercent > 85 ? "⚠️ Dolu" : "✅ Sağlıklı",
                Details = SystemMetrics.DriveSummaryText
            });
            ProgressPercent = 30;

            // 2. Junk Files Analysis (Simulation)
            var junkResults = await _engine.AnalyzeAsync(AllRules, ExcludedPaths.ToList());
            long junkBytes = junkResults.Sum(x => x.SizeBytes);
            TotalDetectedFiles = junkResults.Count;
            TotalDetectedBytes = junkBytes;
            HasAnalyzed = true;

            HealthCheckResults.Add(new HealthCheckStepItem
            {
                Category = "Gereksiz Dosyalar",
                CheckName = "Sistem & Tarayıcı Çöp Taraması",
                StatusBadge = junkBytes > 512L * 1024 * 1024 ? "🧹 Temizlik Önerilir" : "✅ Temiz",
                Details = $"{junkResults.Count:N0} geçici dosya ({CleanerRule.FormatBytes(junkBytes)}) tespit edildi."
            });
            ProgressPercent = 55;

            // 3. Startup items audit
            int startupCount = await Task.Run(() => StartupManagerService.GetStartupItems().Count);
            HealthCheckResults.Add(new HealthCheckStepItem
            {
                Category = "Açılış Hızı",
                CheckName = "Başlangıç (Startup) Uygulamaları",
                StatusBadge = startupCount > 15 ? "⚠️ Yüksek" : "✅ Dengeli",
                Details = $"{startupCount} adet başlangıç kaydı bulundu."
            });
            ProgressPercent = 75;

            // 4. RAM Working Set Boost & DNS Cache Flush
            var (freedRamBytes, trimmedProcCount) = await SystemBoosterAndReportService.OptimizeRamWorkingSetAsync();
            await SystemBoosterAndReportService.FlushDnsCacheAsync();
            RefreshSystemMetrics();
            string reclaimedFormatted = CleanerRule.FormatBytes(freedRamBytes);

            HealthCheckResults.Add(new HealthCheckStepItem
            {
                Category = "Bellek (RAM)",
                CheckName = "Çalışma Kümesi (Working Set) Optimizasyonu",
                StatusBadge = "⚡ Hızlandırıldı",
                Details = $"{trimmedProcCount} işlemden {reclaimedFormatted} önbellek belleği serbest bırakıldı. Güncel kullanım: {SystemMetrics.RamUsedPercent:F0}%"
            });

            HealthCheckResults.Add(new HealthCheckStepItem
            {
                Category = "Ağ & DNS",
                CheckName = "Windows DNS Çözümleyici Önbelleği",
                StatusBadge = "✅ Yenilendi",
                Details = "DNS çözümleyici önbelleği (ipconfig /flushdns) tazelendi."
            });

            UpdateHealthScoreSummary(junkBytes, startupCount);
            ProgressPercent = 100;

            string summary = $"Sağlık Kontrolü Tamamlandı: Skor {HealthScore}/100 ({HealthGradeBadge}) • {reclaimedFormatted} RAM açıldı.";
            StatusMessage = summary;
            AddLog($"[SAĞLIK KONTROLÜ] {summary}");
            TrayNotificationRequested?.Invoke("FreeCleaner Pro — Sağlık Kontrolü", summary, HealthScore < 60);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TickLiveTelemetryAsync()
    {
        if (_disposed || Interlocked.Exchange(ref _telemetrySamplingInProgress, 1) == 1)
            return;

        try
        {
            var ((cpu, ram, downKbps, upKbps), topProcs) = await Task.Run(() =>
                (_telemetrySampler.SampleNext(), FlagshipTelemetryAndThemeService.GetTopProcessesSnapshot(10)));

            if (_disposed)
                return;

            LiveCpuPercent = cpu;
            LiveRamPercent = ram;
            LiveNetDownText = FormatNetRate(downKbps);
            LiveNetUpText = FormatNetRate(upKbps);

            _cpuHistory.Add(cpu);
            _ramHistory.Add(ram);
            _netDownHistory.Add(downKbps);
            _netUpHistory.Add(upKbps);

            TrimHistory(_cpuHistory);
            TrimHistory(_ramHistory);
            TrimHistory(_netDownHistory);
            TrimHistory(_netUpHistory);

            CpuSparklinePoints = FlagshipTelemetryAndThemeService.BuildSparklinePoints(_cpuHistory, 100.0, 260, 74);
            RamSparklinePoints = FlagshipTelemetryAndThemeService.BuildSparklinePoints(_ramHistory, 100.0, 260, 74);
            double maxNet = Math.Max(50.0, Math.Max(_netDownHistory.Max(), _netUpHistory.Max()));
            NetDownSparklinePoints = FlagshipTelemetryAndThemeService.BuildSparklinePoints(_netDownHistory, maxNet, 260, 74);
            NetUpSparklinePoints = FlagshipTelemetryAndThemeService.BuildSparklinePoints(_netUpHistory, maxNet, 260, 74);

            LiveTopProcesses.Clear();
            foreach (var p in topProcs)
                LiveTopProcesses.Add(p);
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("Canlı Telemetri", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _telemetrySamplingInProgress, 0);
        }
    }

    private static void TrimHistory(List<double> list, int maxPoints = 40)
    {
        if (list.Count > maxPoints)
            list.RemoveRange(0, list.Count - maxPoints);
    }

    private static string FormatNetRate(double kbps)
        => kbps >= 1024.0 ? $"{kbps / 1024.0:F2} MB/s" : $"{kbps:F1} KB/s";

    private void PopulateModuleCatalog()
    {
        bool en = LocalizationService.NormalizeLanguageCode(_selectedLanguage) == "en-US";
        AllModuleCatalog.Clear();
        void AddEntry(string key, string trTitle, string enTitle, string glyph, string trCat, string enCat, string keywords)
            => AllModuleCatalog.Add(new NavShortcutItem
            {
                SectionKey = key,
                Title = en ? enTitle : trTitle,
                IconGlyph = glyph,
                CategoryName = en ? enCat : trCat,
                Keywords = keywords
            });

        const string TrCmd = "Hızlı Komut";
        const string EnCmd = "Quick Action";
        const string TrClean = "Temizlik & Disk";
        const string EnClean = "Clean & Disk";
        const string TrPerf = "Performans & Donanım";
        const string EnPerf = "Performance & Hardware";
        const string TrSys = "Sistem & Windows 11";
        const string EnSys = "System & Windows 11";
        const string TrNet = "Ağ & Firewall";
        const string EnNet = "Network & Firewall";
        const string TrSec = "Güvenlik & Gizlilik";
        const string EnSec = "Security & Privacy";
        const string TrDev = "Geliştirici & AI";
        const string EnDev = "Developer & AI";
        const string TrSet = "Ayarlar & Tema";
        const string EnSet = "Settings & Theme";

        // Quick Actions (Ctrl+K)
        AddEntry("CMD:HealthCheck", "⚡ Hızlı Eylem: Tek Tıkla PC Sağlık Kontrolü Başlat", "⚡ Quick Action: Run 1-Click PC Health Check", "\uE95E", TrCmd, EnCmd, "saglik skor health check tarama hizlandir");
        AddEntry("CMD:Analyze", "🔍 Hızlı Eylem: Akıllı Gereksiz Dosya Analizini Başlat", "🔍 Quick Action: Start Smart Junk Analysis", "\uE721", TrCmd, EnCmd, "analiz tara temizlik cop analyze scan junk");
        AddEntry("CMD:OptimizeRam", "🧠 Hızlı Eylem: RAM Belleğini Şimdi Boşalt", "🧠 Quick Action: Optimize RAM Now", "\uE945", TrCmd, EnCmd, "ram bellek bosalt memory boost optimize");
        AddEntry("CMD:FlushDns", "🌐 Hızlı Eylem: DNS Önbelleğini Temizle (flushdns)", "🌐 Quick Action: Flush DNS Cache (flushdns)", "\uE701", TrCmd, EnCmd, "dns flush ip yenile");
        AddEntry("CMD:CheckUpdate", "🔄 Hızlı Eylem: Yeni Sürüm Kontrol Et (GitHub Releases)", "🔄 Quick Action: Check for Updates (GitHub Releases)", "\uE895", TrCmd, EnCmd, "guncelleme update version release github");
        AddEntry("CMD:ProfileSafe", "🛡️ Profil Uygula: Güvenli Günlük Temizlik Kural Seti", "🛡️ Apply Profile: Safe Daily Rule Preset", "\uEA18", TrCmd, EnCmd, "profil safe gunluk daily temizlik kural");
        AddEntry("CMD:ProfileDev", "🛠️ Profil Uygula: Geliştirici Derin (SDK / Cache) Kural Seti", "🛠️ Apply Profile: Developer Deep Rule Preset", "\uE943", TrCmd, EnCmd, "profil developer gelistirici sdk cache nuget npm");
        AddEntry("CMD:ProfileGamer", "🎮 Profil Uygula: Oyuncu Boost (GPU / Shader / Temp)", "🎮 Apply Profile: Gamer Boost Rule Preset", "\uE7FC", TrCmd, EnCmd, "profil gamer oyuncu shader directx steam temp");
        AddEntry("CMD:ProfilePrivacy", "🕵️ Profil Uygula: Maksimum Gizlilik (Tarayıcı / Telemetri)", "🕵️ Apply Profile: Max Privacy Rule Preset", "\uE72E", TrCmd, EnCmd, "profil gizlilik privacy cookie gecmis history");
        AddEntry("CMD:ExportSettings", "📤 Hızlı Eylem: Ayar Profilini JSON Olarak Dışa Aktar", "📤 Quick Action: Export Settings Profile (JSON)", "\uE74E", TrCmd, EnCmd, "ayar yedek export import json profil settings");
        AddEntry("CMD:ExportSupportBundle", "📦 Hızlı Eylem: Teşhis Destek Paketi (.zip) Oluştur", "📦 Quick Action: Export Diagnostic Support Bundle (.zip)", "\uE7B8", TrCmd, EnCmd, "destek teshis zip support bundle diagnostic report");
        AddEntry("CMD:OrganizeDownloads", "🗂️ Hızlı Eylem: İndirilenler Klasörünü Düzenle (Önizleme)", "🗂️ Quick Action: Organize Downloads Folder (Preview)", "\uE8F4", TrCmd, EnCmd, "indirilenler downloads duzenle organize sort undo");

        foreach (CustomCleaningProfile custom in CustomProfiles)
        {
            if (string.IsNullOrWhiteSpace(custom.Name))
                continue;
            AddEntry(
                $"CMD:CustomProfile:{custom.Name}",
                $"⭐ Özel Profil: {custom.Name} ({custom.RuleIds.Count} kural)",
                $"⭐ Custom Profile: {custom.Name} ({custom.RuleIds.Count} rules)",
                "\uEA18",
                TrCmd,
                EnCmd,
                $"custom ozel profil {custom.Name}");
        }

        // All 57 Modules
        AddEntry("Dashboard", "Genel Bakış (Dashboard) & Sağlık Skoru", "Overview (Dashboard) & Health Score", "\uE80F", TrClean, EnClean, "dashboard ana sayfa overview saglik skoru gecmis lifetime forecast");
        AddEntry("DiskTreemap", "🗺️ Görsel Disk Haritası (Treemap + Danışman)", "🗺️ Visual Disk Treemap & Smart Advisor", "\uE8A9", TrClean, EnClean, "treemap harita disk gorsel boyut klasor danisman snapshot diff");
        AddEntry("DeepSpaceRecovery", "💾 Dev Windows Sistem Alanı Kurtarıcısı (15–40 GB)", "💾 Deep Windows Space Recovery (15–40 GB)", "\uEDA2", TrClean, EnClean, "hiberfil winsxs compactos nvidia amd installer deep space");
        AddEntry("SimilarPhotos", "🖼️ Benzer / Tekrarlayan Fotoğraf Bulucu (dHash)", "🖼️ Similar Photo Finder (Perceptual dHash)", "\uEB9F", TrClean, EnClean, "benzer fotograf similar duplicate dhash resim galeri");
        AddEntry("LiveMonitor", "Canlı CPU, RAM & Ağ Grafikleri", "Live CPU, RAM & Network Graphs", "\uE9D9", TrPerf, EnPerf, "canli live grafik cpu ram ag hizi monitor sparkline");
        AddEntry("All", "Akıllı Temizlik (Tümü)", "Smart Cleaner (All Rules)", "\uE770", TrClean, EnClean, "temizlik cop temp gecici smart clean");
        AddEntry("System", "Windows & Sistem Temizliği", "Windows & System Cleanup", "\uE7F8", TrClean, EnClean, "windows temp log recycle bin system");
        AddEntry("Browsers", "Web Tarayıcıları Temizliği", "Web Browsers & AI Cache Cleanup", "\uE774", TrClean, EnClean, "chrome edge firefox brave cache cookie gemini nano optguideondevicemodel weights.bin");
        AddEntry("Apps", "Uygulama & Geliştirici Önbelleği", "Apps & Developer Caches", "\uE943", TrClean, EnClean, "app cache discord steam vscode");
        AddEntry("DiskAnalyzer", "Büyük & Kopya Dosya Bulucu", "Large & Duplicate File Finder (Hardlink)", "\uEDA2", TrClean, EnClean, "buyuk large dosya duplicate sha256 kopya hardlink");
        AddEntry("Downloads", "İndirilenler, Boş Klasör & Düzenleyici", "Downloads, Empty Folders & Organizer", "\uE896", TrClean, EnClean, "downloads kurulum setup iso bos klasor organize sort undo");
        AddEntry("Bloatware", "Bloatware (UWP Appx) Kaldırıcı", "Appx Bloatware Remover", "\uE74D", TrClean, EnClean, "bloatware appx uwp xbox windows 11");
        AddEntry("Shredder", "Dosya Öğütücü (DoD 5220.22-M)", "File Shredder (DoD 5220.22-M)", "\uE74D", TrClean, EnClean, "shredder kalici silme bos alan wipe");
        AddEntry("Startup", "Başlangıç (Startup) Yöneticisi", "Startup Applications Manager", "\uE7E8", TrPerf, EnPerf, "startup acilis baslangic");
        AddEntry("BootPerformance", "Açılış Süresi (Boot Time) Analizi", "Boot Time Bottleneck Analyzer", "\uE916", TrPerf, EnPerf, "boot acilis suresi darbogaz startup");
        AddEntry("Booster", "RAM & Ağ Hızlandırıcı", "RAM & Network Booster", "\uE945", TrPerf, EnPerf, "ram booster hizlandirici dns");
        AddEntry("ProcessPriority", "Canlı CPU Önceliği & Eco Mod", "Live CPU Priority & EcoQoS", "\uEC4A", TrPerf, EnPerf, "cpu oncelik priority eco game boost");
        AddEntry("DiskBench", "SSD / NVMe Disk Hız Testi", "SSD / NVMe Read & Write Benchmark", "\uEC4A", TrPerf, EnPerf, "ssd nvme benchmark okuma yazma hiz");
        AddEntry("SmartPower", "Disk Sağlığı (S.M.A.R.T.) & Güç Planı", "Disk S.M.A.R.T., Battery & Power Plan", "\uE945", TrPerf, EnPerf, "smart disk saglik pil guc oyun modu battery power");
        AddEntry("HardwareSpecs", "Donanım, BIOS & OEM Lisans", "Hardware Inventory, BIOS & OEM Key", "\uE950", TrPerf, EnPerf, "cpu gpu ram bios anakart lisans product key");
        AddEntry("MonitorGpu", "Monitör (Hz) & GPU Doktoru", "Monitor EDID (Hz) & GPU Reset Doctor", "\uE7F4", TrPerf, EnPerf, "monitor edid hz yenileme gpu reset");
        AddEntry("WingetUpdater", "Winget Program Güncelleyici", "Winget Bulk App Updater", "\uE896", TrSys, EnSys, "winget guncelle update yazilim apps");
        AddEntry("Win11Tweaker", "Windows 11 İnce Ayar (Tweaker)", "Windows 11 Ultimate Tweaker", "\uE90F", TrSys, EnSys, "win11 sag tik explorer bing gemini nano chrome ai tweak");
        AddEntry("Registry", "Kayıt Defteri (Registry) Temizleyici", "Registry Scanner & Backup", "\uE90F", TrSys, EnSys, "registry regedit yedek backup");
        AddEntry("Uninstaller", "Program Kaldırıcı & Yetim Kalıntılar", "Program Uninstaller & Orphan Cleaner", "\uE7B8", TrSys, EnSys, "uninstall program kaldir appdata orphan");
        AddEntry("Services", "Windows Servisleri Yöneticisi", "Windows Services Optimizer", "\uE912", TrSys, EnSys, "services hizmetler telemetri");
        AddEntry("RepairCenter", "Sistem Onarım Merkezi (SFC/DISM)", "System Repair Center (SFC / DISM)", "\uE90F", TrSys, EnSys, "sfc dism onarim repair windows.old fontcache");
        AddEntry("RestoreGodMode", "Geri Yükleme Noktası & God Mode", "Restore Points & Windows God Mode", "\uE81C", TrSys, EnSys, "restore point tanri modu god mode");
        AddEntry("ContextMenu", "Sağ Tık Bağlam Menüsü (Shell)", "Shell Context Menu Manager", "\uE700", TrSys, EnSys, "context menu shell sag tik");
        AddEntry("FileAssoc", "Dosya Uzantısı Doktoru (UserChoice)", "File Association Doctor (UserChoice)", "\uE8A5", TrSys, EnSys, "uzanti file association progid default");
        AddEntry("FontManager", "Font (Yazı Tipi) Yöneticisi", "System & User Font Manager", "\uE8D2", TrSys, EnSys, "font yazi tipi ttf otf");
        AddEntry("LanScanner", "Yerel Ağ (LAN / Wi-Fi) Radarı", "Local Network (LAN / Wi-Fi) Radar", "\uE701", TrNet, EnNet, "lan arp ping modem wifi cihaz device");
        AddEntry("WifiDns", "Wi-Fi Şifreleri & DNS Ping Testi", "Saved Wi-Fi Passwords & DNS Ping", "\uE701", TrNet, EnNet, "wifi sifre password dns ping");
        AddEntry("Network", "Canlı Ağ Bağlantıları & Hosts", "Live TCP/UDP Connections & Hosts", "\uE968", TrNet, EnNet, "tcp udp netstat hosts");
        AddEntry("NetworkAdapterDoctor", "Ağ Adaptörü, MAC & Winsock Doktoru", "Network Adapter, MAC & Winsock Doctor", "\uE839", TrNet, EnNet, "mac ip gateway dhcp winsock arp");
        AddEntry("FirewallBlocker", "Firewall & Uygulama İnternet Kesici", "Windows Firewall & 1-Click Blocker", "\uE72E", TrNet, EnNet, "firewall guvenlik duvari engel block");
        AddEntry("PortHunter", "Geliştirici Port Avcısı (Port Killer)", "Developer Port Hunter (Port Killer)", "\uE968", TrNet, EnNet, "port 3000 8080 5000 pid kill");
        AddEntry("DefenderAuditor", "Windows Defender & İstisna Denetçisi", "Defender Shield & Exclusions Auditor", "\uE83D", TrSec, EnSec, "defender antivirus tarama exclusion");
        AddEntry("ExifScrubber", "📸 Fotoğraf EXIF / GPS Gizlilik Temizleyici", "📸 Photo EXIF / GPS Privacy Scrubber", "\uE722", TrSec, EnSec, "exif gps fotograf resim photo gizlilik metadata scrubber");
        AddEntry("ProcessSecurity", "Süreç & Dijital İmza Denetçisi", "Process & X.509 Signature Auditor", "\uEA18", TrSec, EnSec, "imza x509 sha256 unsigned process signature");
        AddEntry("CryptoVault", "AES-256 Dosya Kasası & Çoklu Hash", "AES-256 Vault (.fcvault) & Multi-Hash", "\uE8D7", TrSec, EnSec, "aes256 sifrele encrypt fcvault md5 sha256 sha512");
        AddEntry("Privacy", "Windows Gizlilik & Telemetri Kalkanı", "Windows Privacy & Telemetry Shield", "\uE72E", TrSec, EnSec, "gizlilik privacy mru jumplist telemetri");
        AddEntry("ClipboardShield", "Akıllı Pano & Takip URL Temizleyici", "Smart Clipboard Privacy Shield", "\uE77F", TrSec, EnSec, "clipboard pano utm api key iban");
        AddEntry("BitLocker", "BitLocker & ISO/VHD Sanal Disk", "BitLocker & Mounted ISO/VHD", "\uEDA2", TrSec, EnSec, "bitlocker iso vhd vhdx mount");
        AddEntry("CredentialSsh", "Kimlik Kasası (cmdkey) & SSH", "Credential Vault (cmdkey) & SSH", "\uE8D7", TrSec, EnSec, "ssh credential cmdkey git key");
        AddEntry("UsbForensics", "USB Depolama Geçmişi (USBSTOR)", "USB Storage Forensics (USBSTOR)", "\uE88E", TrSec, EnSec, "usb usbstor adli analiz history");
        AddEntry("CrashLogs", "Mavi Ekran (BSOD) & Olay Günlüğü", "BSOD Crash Dumps & Event Logs", "\uE7BA", TrSec, EnSec, "bsod dmp eventlog cokme crash");
        AddEntry("AiModelCleaner", "Yapay Zeka (AI / LLM) Model Avcısı", "Local AI / LLM Model & Cache Hunter", "\uE99A", TrDev, EnDev, "ollama huggingface lmstudio gguf safetensors ai llm chrome gemini nano optguideondevicemodel weights.bin claude rootfs vhdx");
        AddEntry("DockerWsl", "WSL2 & Docker Yöneticisi", "WSL2 & Docker Cleaner", "\uE756", TrDev, EnDev, "wsl wsl2 docker container prune");
        AddEntry("GitRepoCompressor", "Git Depo (.git) Sıkıştırıcı (git gc)", "Git Repository (.git) Compressor (git gc)", "\uE8B7", TrDev, EnDev, "git gc branch repo");
        AddEntry("DevCleaner", "VS Debug/Retail & DevCleaner", "DevCleaner (VS Debug/Retail, node_modules)", "\uE943", TrDev, EnDev, "visual studio debug release obj node_modules");
        AddEntry("PackageCaches", "NuGet / NPM & WSL2 / Claude VHDX Yöneticisi", "Package Caches & WSL2 / Claude VHDX", "\uE7B8", TrDev, EnDev, "nuget npm pip cargo gradle maven vhdx claude rootfs compact sikistir");
        AddEntry("SqliteOptimizer", "Tarayıcı & IDE SQLite Sıkıştırıcı", "Browser & IDE SQLite Vacuum Optimizer", "\uEE94", TrDev, EnDev, "sqlite veritabani wal chrome vscode cursor vacuum");
        AddEntry("BatchRenamer", "Toplu Dosya Adlandırıcı (PowerRename)", "Batch File Renamer (PowerRename)", "\uE8AC", TrDev, EnDev, "rename toplu adlandirma prefix suffix");
        AddEntry("Unlocker", "Kilitli Dosya Çözücü (File Unlocker)", "Locked File Unlocker (Restart Manager)", "\uE785", TrDev, EnDev, "kilitli dosya locked unlocker restart manager");
        AddEntry("PathDoctor", "PATH Doktoru & Kırık Kısayollar", "Environment PATH & Shortcut Doctor", "\uE950", TrDev, EnDev, "path ortam degiskeni lnk kisayol shortcut");
        AddEntry("TasksAndTree", "Zamanlanmış Görevler & Klasör Ağacı", "Task Scheduler & Folder Size Tree", "\uE823", TrDev, EnDev, "task scheduler klasor boyutu folder size");
        AddEntry("BrowserExtensions", "Tarayıcı Eklentileri Denetçisi", "Browser Extensions Auditor", "\uEA86", TrDev, EnDev, "extension eklenti chrome edge browser");
        AddEntry("DriversUpdates", "KB Güncellemeler & Sürücü Deposu", "Windows Updates (KB) & DriverStore", "\uE777", TrDev, EnDev, "hotfix kb driverstore pnputil drivers");
        AddEntry("Settings", "Tema, Dil, Güncelleme, Beyaz Liste & Raporlar", "Settings, Themes, Language, Portable & Bundles", "\uE713", TrSet, EnSet, "tema theme dil language tr en guncelle update tray portable support zip whitelist ayarlar rapor settings");
    }
}
