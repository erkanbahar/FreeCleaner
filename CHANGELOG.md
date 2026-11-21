# Changelog — FreeCleaner Pro

> 🌐 [English](#english) • [Türkçe](#türkçe)

---

## English

### [4.2.0] - 2026-11-21
#### Added
- **True Portable Mode (`portable.flag`) & Diagnostic Support Bundle (`.zip`)** (`AppStoragePathsService`): stores settings, history, snapshots, telemetry, and logs inside a local `data\` folder when enabled, and exports a redacted support `.zip` (`diagnostic_report.html`, `system_summary.json`, `activity_sanitized.log`, `errors_sanitized.log`).
- **Disk Storage Fill Forecast (`StorageForecastService`)**: tracks daily `C:` utilization series and computes linear growth rate (`+GB/day`) plus estimated days remaining until the 90% critical threshold on the Dashboard.
- **Smart Folder Organizer with Preview & One-Click Undo (`SmartFolderOrganizerService`)**: previews and sorts loose files in Downloads / Desktop into categorized folders with collision-safe naming and JSON undo journal rollback.
- **Full Bilingual Sidebar & Live `Ctrl+K` Command Palette**: localized all remaining sidebar modules (`Loc_Nav*`) and dynamically switches all 67 Command Palette entries + Pinned Favorites between English and Turkish on language change.
- **CI Strict Warnings Gate & 22 Modular Lazy Views**: enforced `-p:TreatWarningsAsErrors=true` in `.github/workflows/dotnet-ci.yml`, extracted 6 more Lazy Views (`BoosterView`, `ShredderView`, `PrivacyView`, `NetworkView`, `DownloadsView`, `BloatwareView`), split `MainViewModel.SystemTools.cs`, and expanded the test suite to 44 passing unit tests.

### [4.1.0] - 2026-11-21
#### Added
- **Single-Instance & Named Pipe IPC Bridge** (`SingleInstanceBridgeService`): forwards Explorer context-menu and CLI arguments directly to the active instance and restores the window cleanly.
- **Windows Explorer Context Menu (`HKCU`) & CLI Launch Flags**: admin-free cascading right-click shortcuts for directories and folder backgrounds (`--treemap`, `--exif`, `--similar`, `--deep-space`, `--health-check`, `--scheduled-maintenance`, `--minimized`).
- **One-Click Cleaning Profiles & JSON Settings Export/Import**: `SafeDaily`, `DeveloperDeep`, `GamerBoost`, and `MaxPrivacy` rule presets + portable JSON settings backup/restore.
- **Zero-Data-Loss NTFS Hardlink Deduplicator** (`NtfsHardlinkService`): Win32 `CreateHardLinkW` + `GetFileInformationByHandle` inode matching, full SHA-256 verification, and atomic temp-backup rollback.
- **Disk Snapshot Diff** (`DiskSnapshotDiffService`): save Treemap folder snapshots and compute per-folder `+GB` growth deltas between scans.
- **Smart Process Lock Guard** (`ProcessLockGuardService`): detects running browsers/apps before cleanup and `Claude Desktop` / `Docker` / `WSL2` before `.vhdx` compaction with safe Close & Continue prompts.
- **In-App GitHub Releases Updater with SHA-256 Verification** (`AppUpdateService` + `.github/workflows/release.yml` `SHA256SUMS.txt` generation).
- **Live System Tray Telemetry Tooltip & Cooldown Alerts**: 63-char live CPU/RAM/Disk tooltip + 30-min cooldown warnings, plus **Silent Auto-Start to Tray (`--minimized` via HKCU Run)**.
- **Windows 11 Manifest & DWM Polish**: `app.manifest` (`PerMonitorV2` DPI, `longPathAware`) + `WindowsShellVisualService` rounded corners & adaptive dark/light frame.
- **Upgraded Diagnostic HTML Report & Activity Console Tools**: inline SVG health gauge & 14-day cleanup chart in HTML reports + live search, copy, `.log` export, and clear controls in the Activity Console.
- **Full Bilingual Localization & Modular Architecture**: 16 Lazy Views (`LazyViewRegistry`), partial `MainViewModel` split (`CoreTools`, `NextGen`, `PowerSuite`), and 39 passing unit tests.

### [4.0.0] - 2026-11-15
- Initial `.NET 10 WPF` flagship release featuring 57 modules across 6 categories, Interactive Treemap + Smart Advisor, Deep Space Recovery, Perceptual `dHash` Similar Photos, EXIF/GPS Scrubber, 4 live themes, and `Ctrl+K` Command Palette.

---

## Türkçe

### [4.2.0] - 2026-11-21
#### Eklenenler ve İyileştirmeler
- **Gerçek Taşınabilir Mod (`portable.flag`) & Teşhis Destek Paketi (`.zip`)** (`AppStoragePathsService`): Etkinleştirildiğinde tüm ayar, geçmiş, snapshot, telemetri ve log dosyalarını `.exe` yanındaki `data\` klasöründe tutar; hassas token/şifre ifadelerinden arındırılmış tek dosyalık destek arşivi üretir.
- **Disk Doluluk Tahmini (`StorageForecastService`)**: Günlük `C:` kullanım eğilimini analiz ederek günlük ortalama büyümeyi ve `%90` kritik eşiğine tahmini kalan günü Dashboard'da gösterir.
- **Önizlemeli & Geri Alınabilir Akıllı Klasör Düzenleyici (`SmartFolderOrganizerService`)**: İndirilenler / Masaüstü içindeki dağınık dosyaları uzantıya göre alt klasörlere ayırır ve tek tıkla geri alma sunar.
- **Tam Çift Dilli Sol Menü & Canlı `Ctrl+K` Komut Paleti**: Kalan tüm sol menü öğeleri yerelleştirildi ve dil değişikliğinde tüm 67 Komut Paleti öğesi ile favoriler anında güncellenir.
- **CI Katı Uyarı Kapısı & 22 Modüler Lazy View**: `.github/workflows/dotnet-ci.yml` içine `-p:TreatWarningsAsErrors=true` eklendi, 6 ekran daha (`BoosterView`, `ShredderView`, `PrivacyView`, `NetworkView`, `DownloadsView`, `BloatwareView`) ayrıştırıldı, `MainViewModel.SystemTools.cs` bölündü ve test sayısı 44'e çıkarıldı.

### [4.1.0] - 2026-11-21
#### Eklenenler ve İyileştirmeler
- **Tek Örnek (Single-Instance) & Named Pipe IPC Köprüsü** (`SingleInstanceBridgeService`): Gezgin sağ tık veya CLI üzerinden gelen argümanları açık olan ana pencereye iletir ve pencereyi öne getirir.
- **Windows Gezgini Sağ Tık Menüsü (`HKCU`) & CLI Desteği**: Yönetici izni gerektirmeden klasör sağ tık menüsüne Treemap, EXIF/GPS Temizleyici ve Benzer Fotoğraf kısayolları ekler/kaldırır (`--treemap`, `--exif`, `--similar`, `--deep-space`, `--health-check`, `--scheduled-maintenance`, `--minimized`).
- **Tek Tıkla 4 Akıllı Temizlik Profili & JSON Yedekleme**: `Güvenli Günlük`, `Geliştirici`, `Oyuncu`, `Maksimum Gizlilik` profilleri ve taşınabilir JSON ayar dışa/içe aktarma.
- **Sıfır Veri Kayıplı NTFS Hardlink Birleştirici** (`NtfsHardlinkService`): `CreateHardLinkW`, inode kontrolü, tam SHA-256 doğrulaması ve atomik geri alma koruması ile birebir kopyalardan gerçek disk alanı kurtarır.
- **Klasör Boyutu Anlık Görüntü Karşılaştırması (`DiskSnapshotDiffService`)**: Treemap anlık görüntülerini kaydedip `+GB` büyüme farklarını sıralar.
- **Akıllı Süreç Kilit Koruması (`ProcessLockGuardService`)**: Temizlik öncesi açık tarayıcı/uygulamaları, `.vhdx` sıkıştırma öncesi `Claude Desktop` / `Docker` / `WSL2` süreçlerini algılayıp güvenli kapatma seçeneği sunar.
- **Uygulama İçi SHA-256 Doğrulamalı Güncelleyici** (`AppUpdateService` & `SHA256SUMS.txt` üretimi).
- **Canlı Sistem Tepsisi Telemetrisi, Cooldown Uyarıları & Windows ile Sessiz Başlatma (`--minimized`)**.
- **Windows 11 `app.manifest` (`PerMonitorV2` DPI, `longPathAware`), DWM Yuvarlatılmış Pencere Cilası & Sürüm Metadatası**.
- **SVG Sağlık Göstergeli HTML Rapor & İşlem Günlüğü Filtreleme / Panoya Kopyalama / `.log` Dışa Aktarma**.
- **Çekirdek Ekranlar İçin Tam TR/EN Yerelleştirme, 16 Modüler Lazy View & 39 Unit Test**.

### [4.0.0] - 2026-11-15
- 6 kategoride 57 araç, Görsel Treemap + Akıllı Danışman, Dev Alan Kurtarıcısı, `dHash` Benzer Fotoğraflar, EXIF/GPS Temizleyici, 4 canlı tema ve `Ctrl+K` Komut Paleti ile ilk `.NET 10 WPF` sürümü.
