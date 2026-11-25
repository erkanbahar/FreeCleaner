# FreeCleaner Pro v5.2 (.NET 10 WPF)

[![FreeCleaner .NET 10 CI](https://github.com/erkanbahar/FreeCleaner/actions/workflows/dotnet-ci.yml/badge.svg)](https://github.com/erkanbahar/FreeCleaner/actions/workflows/dotnet-ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10.0--windows-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/Windows-10%20%7C%2011%20(x64%20%7C%20arm64)-0078D6?logo=windows)
![Modules](https://img.shields.io/badge/Modules-57%20Tools-10B981)
![Tests](https://img.shields.io/badge/Tests-141%20Passing-22C55E)
![Language](https://img.shields.io/badge/Language-English%20%7C%20T%C3%BCrk%C3%A7e-38BDF8)
![License](https://img.shields.io/badge/License-MIT-blue)

> 🌐 **Language / Dil:** [English](#english) • [Türkçe](#türkçe) • 📋 [Changelog](CHANGELOG.md) • 🔏 [Code signing policy](CODE_SIGNING_POLICY.md) • 🔒 [Privacy policy](PRIVACY.md)

**Download:** [GitHub Releases](https://github.com/erkanbahar/FreeCleaner/releases/latest) — free and open source (MIT).

---

## English

**FreeCleaner Pro** is a comprehensive, **57-module** Windows system maintenance, disk recovery, performance optimization, security/privacy, and developer utility suite built with **.NET 10 (`net10.0-windows`)** and **WPF (Modern Fluent UI)** — with **zero third-party UI dependencies**.

### Flagship & Power-User Features

- **🔁 Single-Instance & Hardened Named Pipe IPC Bridge (`Mutex` + `NamedPipeServerStream`):** Prevents duplicate tray/window launches; secondary invocations (including Windows Explorer right-click actions) forward bounded CLI arguments (`<= 16 KiB` / `<= 32` args) over `CurrentUserOnly`-isolated named pipes to the primary window and restore it to front.
- **🖱️ Windows Explorer Right-Click Menu (`HKCU`), Silent Tray Auto-Start (`--minimized`) & CLI:** Install/remove cascading Explorer context-menu shortcuts under `HKCU` without Administrator rights, or enable Windows startup directly to System Tray via `--minimized`.
- **🛡️ Built-In & User-Defined Cleaning Profiles + First-Run Setup Wizard:** Instantly apply **Safe Daily**, **Developer Deep**, **Gamer Boost**, or **Max Privacy** presets, save/apply/delete your own **Custom Cleaning Profiles** (`CustomCleaningProfile`) from Smart Cleaner, `Ctrl+K`, or CLI (`--profile <CustomName>`), and configure Language, Theme, Recycle Bin safety, and Portable Mode on first launch (re-openable from Settings) alongside portable `.json` settings backup/restore.
- **🔗 Zero-Data-Loss NTFS Hardlink Deduplicator:** Merges byte-for-byte identical duplicate files (`CreateHardLinkW` + `GetFileInformationByHandle` inode checks + full SHA-256 verification + same-NTFS-volume safety guard + atomic swap/rollback) so disk space is reclaimed while keeping every file path accessible.
- **📈 Disk Snapshot Diff ("Where Did My Disk Space Go?"):** Saves lightweight Treemap snapshots (`%LocalAppData%\FreeCleaner\snapshots`) and computes per-folder `+GB` growth deltas (`ComputeGrowthDiff`) between scans.
- **🔒 Smart Browser / VM Process Lock Guard & Masked CryptoVault:** Detects active browsers (`Chrome`, `Edge`, `Brave`, `Firefox`, `Discord`, `Steam`, `VS Code`) before cleanup and `Claude Desktop` / `Docker` / `WSL2` before `.vhdx` compaction, while streaming 600K-iteration `FCVAULT2` file encryption features a masked `PasswordBox` with reveal toggle, live strength meter, and post-operation auto-clear.
- **⬇️ Hardened In-App GitHub Releases Updater (`SocketsHttpHandler`, Redirect & Zip-Slip Guards):** Runs an optional silent daily update check on launch (configurable in Settings), displays theme-adaptive in-app Toast notifications with quick actions, prioritizes `FreeCleaner.exe` / portable ZIPs while skipping `Setup` & `Winget` manifests (`SelectPortableReleaseAsset`), enforces trusted HTTPS GitHub hosts on both initial and post-redirect `RequestUri`s (`IsTrustedDownloadUri`), validates cryptographic `SHA-256` digests (`SHA256SUMS.txt` / GitHub asset digest with fail-closed `requireHash`), guards ZIP extraction against Zip-Slip traversal, runs an atomic backup/swap/relaunch script, and cleans up stale `FreeCleaner.exe.old` binaries on startup.
- **🔔 Live System Tray Telemetry, Cooldown & Proactive Storage Forecast Alerts:** Displays real-time `CPU %`, `RAM %`, and `C:` free GB in the tray tooltip (63-char safe), supports minimize-to-tray, sends cooldown-protected critical alerts (`>= 90%` Disk / `>= 92%` RAM), and notifies proactively when predicted days to 90% disk usage fall below your configurable threshold (`7 / 15 / 30` days — click balloon to open Smart Cleaner).
- **🛡️ Global Crash Guard (`CrashGuardService`):** Captures unhandled UI/AppDomain/Task exceptions cleanly, writes redacted `crashes/crash-*.log` reports (`%USERPROFILE%` + secret sanitization), and offers one-click redacted Desktop Support Bundle `.zip` export (`latest_crash_sanitized.log` included).
- **📊 SVG Diagnostic HTML Report & Filterable Activity Console:** Exports rich HTML/JSON reports featuring an inline SVG health-score gauge and 14-day cleanup history bars, paired with live log search, clipboard copy, `.log` export, and clear buttons in the Activity Console.
- **🪟 Native Windows 11 DWM & Manifest Polish:** Includes `app.manifest` (`PerMonitorV2` DPI awareness and `longPathAware` for deep `node_modules`/cache paths) plus DWM rounded corners and theme-adaptive frame attributes.
- **🎒 Zero-Footprint Portable Mode (`portable.flag`), Unified Backup Retention & Support Bundle (`.zip`):** Keeps settings, history, snapshots, telemetry, logs, reports, font backups, and Registry/Hosts/SQLite backups strictly inside local `data\` when enabled; lets you list, restore, delete, or prune old backups in one center; and exports a redacted support `.zip` (`diagnostic_report.html`, `system_summary.json`, sanitized logs).
- **🩺 Dashboard, Lifetime Saved Space, Storage Fill Forecast & 100-Point Health Check:** Scores disk usage, junk load, startup items, and RAM; tracks **Lifetime Reclaimed Space**, a 14-day cleanup chart, and linear `C:` storage growth forecast (`+GB/day` & days to 90%).
- **🗺️ Interactive Disk Treemap & Smart File Advisor:** Fast Win32 `FileSystemEnumerator` scanning visualizes large folders in seconds and classifies 30+ known targets (`OptGuideOnDeviceModel`, `Claude-Data rootfs.vhdx`, `Docker VHDX`, `hiberfil.sys`, `node_modules`) with safe recycle or protection guidance.
- **💾 Deep Windows Space Recovery & VHDX Compactor:** Manages `hiberfil.sys` (`powercfg`), `WinSxS` (`DISM`), lossless `CompactOS`, `NVIDIA/AMD` installer caches, and one-click DiskPart `compact vdisk` for WSL2 / Docker / Claude VM `.vhdx` virtual disks.
- **🖼️ Perceptual Similar Photo Finder (`dHash`), 📸 EXIF Scrubber & 🗂️ Smart Folder Organizer:** Clusters rotated/EXIF-tagged burst photos via `LockBits` 64-bit `dHash` (`SimilarPhotoFinderService`), strips GPS/EXIF metadata pixel-safely, and organizes Downloads / Desktop folders with one-click Undo.
- **📈 Live CPU / RAM & Network Sparkline Monitor:** 1-second Win32 `GetSystemTimes` + `NetworkInterface` telemetry with sparkline charts and top-10 memory consumers.
- **⌨️ 100% Runtime Bilingual UI (`57/57` Views + `CleanerRule` + Tray + Themed Dialogs), `SystemAuto` Theme & Accessibility:** Live switching across `SystemAuto` (synced dynamically with Windows Light/Dark mode via `AppsUseLightTheme`), `ObsidianNeon`, `MidnightViolet`, `EmeraldMatrix`, and `FluentLight` themes; full `en-US` / `tr-TR` translation across **all 57/57 Lazy Views**, all 20 default `CleanerRule` entries/risk labels, injectable theme-adaptive `IUserDialogService` / `ThemedDialogWindow` dialogs (`TranslateDialogTitle`), Command Palette, and System Tray (`Free` vs `Boş`); plus **Window Placement Memory**, Toast overlays, **`F1` Keyboard Shortcuts Guide** (`Ctrl+K`, `Ctrl+F`, `Ctrl+,`, `Ctrl+1..6`, `F5`, `F1`, `Esc`), `IsKeyboardFocused` button/checkbox focus rings, and **`57/57` view `AutomationProperties.Name` screen-reader coverage**.
- **📦 ReadyToRun Installer, Supply-Chain Security & MIT Governance:** Uses Central Package Management (`Directory.Packages.props` + `Microsoft.Data.Sqlite` `10.0.0`), `AnalysisLevel=latest-recommended` `.NET Analyzers`, `NuGetAudit`, CodeQL analysis, SLSA `attest-build-provenance`, `LibraryImport` + `DllImportSearchPath.System32` Win32 interop, streaming 600K-iteration `FCVAULT2` encryption with memory zeroization, pre/post-VACUUM SQLite `PRAGMA quick_check` integrity guard + backup rollback, crash-safe `AtomicFileStorageService` (with `.bak` recovery across settings, history, organizer, snapshots, storage forecast, and CLI/config exports), background `TickLiveTelemetryAsync`, `FluentLight` brush hygiene, `AsyncRelayCommand` + `IDisposable` cleanup, unified deadlock-free concurrent `SafeProcessRunner`, junction/system-path shredder guards, `[GeneratedRegex]`, global `DataGrid` recycling virtualization, rotating redacted `ErrorLogService`, native `win-x64` & `win-arm64` builds, `installer/FreeCleaner.iss`, `PublishReadyToRun` builds, auto-hashed `FreeCleaner-Winget-Manifests-*.zip`, `LICENSE`, `SECURITY.md`, `CONTRIBUTING.md`, **101 passing tests** (`39.41%` line / `26.34%` branch Cobertura coverage with a `36.0%` CI floor), and CI `-p:TreatWarningsAsErrors=true`.

### All Modules (6 Categories • 57 Tools)

#### 1. Cleaning & Disk Recovery (12 Modules)
1. **Overview (Dashboard), Lifetime History & 100-Point Health Check**
2. **Interactive Disk Treemap & Smart File Advisor**
3. **Deep Windows Space Recovery (`hiberfil.sys`, `WinSxS`, `CompactOS`, `NVIDIA/AMD Installer`)**
4. **Similar / Near-Duplicate Photo Finder (64-bit Perceptual `dHash` + Preview)**
5. **Smart Cleaning (All Rules)** — Simulation preview, presets & Recycle Bin safety
6. **Windows & System Cleanup** — Temp, Prefetch, Windows Update, Crash Dumps & Recycle Bin
7. **Web Browsers & On-Device AI Models** — Chrome (`OptGuideOnDeviceModel`/`weights.bin`), Edge, Brave, Firefox
8. **App & Developer Caches** — Discord, Steam, VS Code, etc.
9. **Large & Duplicate File Analyzer** — SHA-256 deduplication & Zero-Data-Loss NTFS Hardlink Merge
10. **Downloads Assistant & Empty Folder Cleaner**
11. **Windows 10/11 Appx Bloatware Uninstaller**
12. **Military-Grade File Shredder (`DoD 5220.22-M`) & Free Space Wiper**

#### 2. Performance, Startup & Hardware (9 Modules)
13. **Live CPU, RAM & Network Sparkline Monitor**
14. **Startup Manager** (`HKCU` / `HKLM` + Startup folder)
15. **Windows Boot Time Analyzer**
16. **Live RAM Optimizer (`EmptyWorkingSet`) & Network Booster**
17. **Live Process CPU Priority & Efficiency Mode (`EcoQoS`)**
18. **SSD / NVMe Live Sequential Read & Write Benchmark**
19. **Disk Health (`S.M.A.R.T.`), Battery Report & Ultimate Performance Power Plan**
20. **Deep Hardware Inventory, BIOS & OEM Windows Product Key**
21. **Monitor EDID, Refresh Rate (`Hz`) & GPU Reset Doctor**

#### 3. System Repair & Windows 11 (10 Modules)
22. **Winget Batch App Updater** (live progress & cancellation)
23. **Windows 11 Ultimate Tweaker** — Classic context menu, file extensions, Explorer speedups
24. **Registry Scanner, Automatic `.reg` Backup & Repair**
25. **Program Uninstaller & Orphan `AppData` Cleaner**
26. **Windows Services & Telemetry Optimizer**
27. **System Repair Center (`SFC /scannow`, `DISM`, `FontCache`, `Windows.old`)**
28. **System Restore Point & Windows God Mode Center**
29. **Shell Context Menu Manager**
30. **File Extension & Default Program (`UserChoice`) Doctor**
31. **System & User Font Manager**

#### 4. Network, Wi-Fi & Firewall (6 Modules)
32. **LAN / Wi-Fi Device Radar & ARP/Ping Scanner**
33. **Saved Wi-Fi Password Viewer & Live DNS Ping Benchmark**
34. **Live TCP/UDP Connections & `Hosts` File Manager**
35. **Network Adapter, IP/MAC & `TCP/IP Winsock` Repair Doctor**
36. **Windows Firewall Auditor & One-Click App Blocker**
37. **Developer Port Hunter (`Port Killer` — 3000/5000/8080/PID)**

#### 5. Security, Privacy & Forensics (10 Modules)
38. **Photo EXIF / GPS Privacy Scrubber & Optimizer**
39. **Windows Defender Shield & Hidden Exclusion (`ExclusionPath`) Auditor**
40. **Process & Digital Signature (`X.509` + `SHA-256`) Auditor**
41. **AES-256-GCM File Encryption Vault (`.fcvault`) & Multi-Hash Verifier**
42. **Windows Privacy & Telemetry Shield (`MRU`, `JumpList`, `Activity History`)**
43. **Smart Clipboard Shield & Tracking URL Cleaner**
44. **BitLocker Status Auditor & Mounted `ISO / VHD` Manager**
45. **Windows Credential Vault (`cmdkey`) & `SSH / Git` Key Auditor**
46. **USB Storage Forensics (`USBSTOR`) & History Cleaner**
47. **BSOD Crash Dumps (`.dmp`) & `EventLog` Analyzer**

#### 6. Developer, AI & Productivity (10 Modules)
48. **Local AI / LLM Model & Cache Hunter** (`Ollama`, `HuggingFace`, `LM Studio`)
49. **WSL2 Distro Manager & Docker Container / Image Cleaner**
50. **Git Repository (`.git`) Compressor (`git gc`) & Status Radar**
51. **DevCleaner & Gamer** — Visual Studio `Debug/Retail/Release/obj/.vs`, `node_modules`, `.venv`, DirectX/NVIDIA/AMD shaders
52. **Global Package Caches (`NuGet`, `NPM`, `Pip`, `Cargo`, `Gradle`) & `WSL2 / Claude VHDX` Compactor**
53. **Browser & IDE `SQLite` Database Optimizer (`VACUUM`)**
54. **Batch File Renamer (`PowerRename` & Undo)**
55. **Locked File Unlocker (`Win32 Restart Manager API`)**
56. **Environment `PATH` Doctor & Broken Shortcut (`.lnk`) Cleaner**
57. **Task Scheduler, Browser Extensions, `KB` / `DriverStore`, Exclusion Whitelist & HTML/JSON Reporting**

### CLI Startup Arguments & Keyboard Shortcuts

| CLI Argument | Action |
| :--- | :--- |
| `--treemap "<path>"` | Opens Disk Treemap and scans the specified folder |
| `--exif "<path>"` | Opens EXIF/GPS Scrubber and scans photos in the specified folder |
| `--similar "<path>"` | Opens Similar Photo Finder (`dHash`) on the specified folder |
| `--deep-space` | Opens Deep Windows Space Recovery and starts analysis |
| `--health-check` | Runs the 100-Point Health Check on Dashboard |
| `--scheduled-maintenance` | Executes headless/auto scheduled cleanup and closes on completion |
| `--dry-run` | Analyzes cleanup rules safely without deleting any files |
| `--profile <SafeDaily\|DeveloperDeep\|GamerBoost\|MaxPrivacy\|CustomName>` | Applies a built-in preset or user-defined custom profile on launch |
| `--report-json "<path>"` | Exports the structured JSON scan breakdown (`CliAutomationReport`) |
| `--exit` / `--headless` | Exits cleanly right after CLI automation finishes |
| `--minimized` / `--tray` | Starts silently minimized directly to System Tray |

| Shortcut | Action |
| :--- | :--- |
| `Ctrl + K` | Open / Close Quick Command & Module Palette |
| `Ctrl + F` | Focus Module Navigation Search box |
| `Ctrl + ,` | Open Settings (Whitelist, Theme, Language, Updates) |
| `Ctrl + 1` .. `Ctrl + 6` | Quick jump: Dashboard / Cleaner / Treemap / Live Monitor / Winget / Settings |
| `F5` | Refresh or re-analyze active module |
| `↑` / `↓` + `Enter` | Select and run a module or profile in Command Palette |
| `Esc` | Close First-Run Setup Overlay or Command Palette |

### Build, Test & Portable Release

#### Requirements
- **.NET 10 SDK** (`net10.0-windows`)
- **Windows 10 / Windows 11 (x64 & ARM64)**

#### CLI Build & Unit Tests (89 Tests + Coverage)
```powershell
dotnet restore FreeCleaner.slnx
dotnet build FreeCleaner.slnx --configuration Release --no-restore -p:TreatWarningsAsErrors=true
dotnet test FreeCleaner.Tests/FreeCleaner.Tests.csproj --configuration Release --no-build --collect:"XPlat Code Coverage"
dotnet run --project FreeCleaner/FreeCleaner.csproj
```

#### UI Smoke Test
```powershell
.\scripts\smoke-test.ps1 -Screenshots   # visits every section in TR + EN; report in %TEMP%\FreeCleaner-smoke
```
Nothing is deleted: dialogs are answered "No"/"Cancel" automatically and settings/logs go to a temporary data folder.

#### Single-File Portable `.exe` (`win-x64` / `win-arm64`) & Windows Installer (`Setup .exe`)
```powershell
.\scripts\Publish-Portable.ps1
# Optional ARM64 single-file build, ReadyToRun x64, USB portable + Winget manifest preview:
.\scripts\Publish-Portable.ps1 -Runtime win-arm64
.\scripts\Publish-Portable.ps1 -ReadyToRun -IncludePortableFlag -GenerateWingetManifests
# Optional Inno Setup 6 Windows Installer (dist/FreeCleaner-Setup-win-x64-v4.8.0.exe):
ISCC.exe /DMyAppVersion=4.8.0 /DSourceExe=..\artifacts\portable\FreeCleaner.exe installer\FreeCleaner.iss
```
Outputs `artifacts/portable/FreeCleaner.exe` (or `artifacts/portable-arm64/FreeCleaner.exe`). Pushing a `v*` tag (e.g., `git tag v4.8.0 && git push origin v4.8.0`) triggers `.github/workflows/release.yml` to verify tests, build `PublishReadyToRun` x64 + native ARM64 executables with warnings-as-errors, generate SLSA build provenance attestations, and publish Standard Portable `.zip` (`win-x64` & `win-arm64`), USB (`portable.flag`) `.zip`, Windows Setup `.exe` installer, pre-hashed `FreeCleaner-Winget-Manifests-*.zip`, `FreeCleaner.exe`, and `SHA256SUMS.txt` on GitHub Releases.

---

### Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org) (application pending; until approval, releases are unsigned and verified with `SHA256SUMS.txt` and SLSA attestations).
Roles: committer, reviewer and signing approver — [Erkan Bahar (@erkanbahar)](https://github.com/erkanbahar). Details: [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).
Privacy: no telemetry; the only automatic network request is the daily update check against GitHub, which can be switched off — see [PRIVACY.md](PRIVACY.md).

---

## Türkçe

[⬆️ English](#english)

**FreeCleaner Pro**, **.NET 10 (`net10.0-windows`)** ve **WPF (Modern Fluent UI)** altyapısıyla geliştirilmiş, sıfır üçüncü taraf UI bağımlılığına sahip, **57 modüllü** kapsamlı bir Windows sistem temizleme, disk kurtarma, performans optimizasyonu, güvenlik/gizlilik ve geliştirici bakım paketidir.

### Öne Çıkan Amiral Gemisi Özellikler

- **🔁 Tek Örnek (Single-Instance) & Sertleştirilmiş Named Pipe IPC Köprüsü:** Uygulama sistem tepsisinde açıkken ikinci bir kopya açılmasını önler; Gezgin sağ tık veya CLI parametrelerini `CurrentUserOnly` erişim kısıtlı ve boyut sınırlı (`<= 16 KiB` / `<= 32` argüman) named pipe üzerinden mevcut pencereye iletir.
- **🖱️ Windows Gezgini Sağ Tık Menüsü (`HKCU`), Sessiz Başlatma (`--minimized`) & CLI:** Yönetici izni olmadan `HKCU` altında sağ tık menüsü kurup kaldırır veya Windows başlangıcında `--minimized` ile doğrudan sistem tepsisine gizli başlatır.
- **🛡️ Hazır & Kullanıcı Tanımlı Özel Temizlik Profilleri + İlk Kurulum Sihirbazı:** **Güvenli Günlük**, **Geliştirici**, **Oyuncu** ve **Maksimum Gizlilik** önayarlarının yanı sıra Akıllı Temizlik içinden kendi **Özel Temizlik Profillerinizi** (`CustomCleaningProfile`) kaydedin, uygulayın, silin veya `Ctrl+K` / `--profile <OzelAd>` ile çalıştırın; ilk açılışta Dil, Tema, Geri Dönüşüm ve Taşınabilir Mod kurulumunu tamamlayın (Ayarlar'dan yeniden açılabilir) ve dışlama listesi, kural seçimleri, tema, dil ve sabitlenen modülleri `.json` ile yedekleyin.
- **🔗 Sıfır Veri Kayıplı NTFS Hardlink Birleştirici:** Aynı NTFS sürücüsündeki birebir kopya dosyaları (`CreateHardLinkW` + `GetFileInformationByHandle` inode kontrolü + tam SHA-256 doğrulaması + atomik geri alma koruması) tek fiziksel bloğa bağlayarak disk alanı açarken tüm dosya yollarını yerinde korur.
- **📈 Klasör Boyutu Anlık Görüntü Karşılaştırması (Disk Snapshot Diff):** Treemap taramalarını hafif anlık görüntüler (`%LocalAppData%\FreeCleaner\snapshots`) olarak kaydeder; önceki taramaya göre en çok büyüyen klasörleri (`+GB` artış) listeler.
- **🔒 Akıllı Süreç Kilit Koruması & Maskeli CryptoVault Kasa:** Temizlik öncesinde açık tarayıcı/uygulamaları (`Chrome`, `Edge`, `Brave`, `Firefox`, `Discord`, `Steam`, `VS Code`), `.vhdx` sıkıştırma öncesinde ise `Claude Desktop` / `Docker` / `WSL2` süreçlerini algılayıp güvenli kapatma seçeneği sunarken; 600K iterasyonlu `FCVAULT2` şifrelemede maskeli `PasswordBox`, göster/gizle (`👁`), parola gücü göstergesi ve işlem sonrası otomatik parola temizliği sağlar.
- **⬇️ Sertleştirilmiş Uygulama İçi Güncelleyici (`SocketsHttpHandler`, Redirect & Zip-Slip Korumalı):** Açılışta günde bir kez sessiz sürüm denetimi yapar (Ayarlar'dan yönetilebilir), yeni sürümde uygulama içi Toast bildirimi sunar, `Setup`/`Winget` paketlerini eleyip öncelikli olarak `FreeCleaner.exe`/taşınabilir `.zip` dosyasını seçer (`SelectPortableReleaseAsset`), TLS 1.2/1.3 `SocketsHttpHandler` kullanıp hem ilk bağlantıda hem HTTP yönlendirmesi sonrası nihai `RequestUri`'de güvenilir HTTPS GitHub host'larını (`IsTrustedDownloadUri`) zorunlu kılar, fail-closed `SHA-256` bütünlüğünü (`SHA256SUMS.txt` / asset digest) doğrular, ZIP çıkarımında Zip-Slip yol saldırılarını engeller ve açılışta eski `FreeCleaner.exe.old` artıklarını otomatik temizler.
- **🔔 Canlı Sistem Tepsisi Telemetrisi, Bekleme Süreli & Proaktif Depolama Tahmin Uyarıları:** Tepsi ikonu üzerine gelince anlık `CPU %`, `RAM %` ve `C:` boş alanı (63 karakter sınırına uygun) gösterir; Disk (`>= %90`) veya RAM (`>= %92`) kritik eşiğe ulaştığında 30 dakikalık cooldown ile uyarır; disk doluluk tahmini belirlenen kalan gün eşiğinin (`7 / 15 / 30` gün) altına düştüğünde proaktif bildirim üretir (bildirime tıklayınca Akıllı Temizlik açılır).
- **🛡️ Global Çökme Koruması (`CrashGuardService`):** Beklenmedik UI, `AppDomain` ve arka plan `Task` hatalarını yakalar; `%USERPROFILE%` ve gizli anahtarlardan arındırılmış dökümleri `crashes/crash-*.log` altına yazar (en fazla 20 log saklar) ve Masaüstüne `latest_crash_sanitized.log` içeren Teşhis Destek Paketi `.zip` oluşturulmasını önerir.
- **📊 SVG Sağlık Göstergeli HTML Rapor & Filtrelenebilir Konsol:** Inline SVG sağlık puanı kadranı ve 14 günlük geçmiş çubukları içeren HTML/JSON rapor üretir; işlem konsolunda canlı arama, panoya kopyalama, `.log` dışa aktarma ve temizleme araçları sunar.
- **🪟 Windows 11 Yerel Pencere Cilası & `app.manifest`:** `PerMonitorV2` DPI, `longPathAware` uzun yol desteği ve DWM yuvarlatılmış pencere/uyumlu çerçeve özellikleri içerir.
- **🎒 Tam Sıfır İzli Taşınabilir Mod (`portable.flag`), Birleşik Yedekleme Merkezi & Destek Paketi (`.zip`):** Etkinleştirildiğinde tüm ayar, geçmiş, snapshot, telemetri, log, rapor, font yedekleri ve Registry/Hosts/SQLite yedeklerini yalnızca uygulama yanındaki `data\` klasöründe tutar; yedekleri tek merkezden geri yükleme, silme veya budama (pruning) ile yönetir ve hassas verilerden arındırılmış destek `.zip` arşivi dışa aktarır.
- **🩺 Genel Bakış (Dashboard), Ömür Boyu Kazanılan Alan, Doluluk Tahmini & 100 Puanlık Sağlık Kontrolü:** Disk doluluğu, çöp yükü, başlangıç uygulamaları ve RAM'i puanlar; **Ömür Boyu Kurtarılan Alan**, 14 günlük grafik ve `C:` için doğrusal dolma tahminini (`+GB/gün` & `%90`'a kalan gün) gösterir.
- **🗺️ Etkileşimli Görsel Disk Haritası (Treemap + Akıllı Dosya Danışmanı):** Doğrudan Win32 `FileSystemEnumerator` ve 2 seviyeli paralel taramayla dev klasörleri saniyeler içinde görselleştirir; `OptGuideOnDeviceModel`, `Claude-Data rootfs.vhdx`, `Docker VHDX`, `hiberfil.sys`, `node_modules` gibi 30+ bilinen yapıyı tanıyıp **Güvenle Geri Dönüşüm'e Gönder** veya koruma önerisi sunar.
- **💾 Dev Windows Sistem Alanı Kurtarıcısı & VHDX Sıkıştırıcı:** Hazırda bekletmeyi kapatma/yarıya indirme (`powercfg`), bileşen deposu temizliği (`DISM`), kayıpsız `CompactOS` sıkıştırma, `NVIDIA/AMD` sürücü artıkları ve WSL2/Docker/Claude VM `.vhdx` sanal diskleri için tek tıkla DiskPart `compact vdisk` desteği sunar.
- **🖼️ Benzer Fotoğraf Bulucu (`dHash`), 📸 EXIF Temizleyici & 🗂️ Akıllı Klasör Düzenleyici:** `LockBits` 64-bit `dHash` + EXIF yönlendirme normalizasyonuyla seri ve döndürülmüş fotoğrafları düşük bellekle kümeler, EXIF/GPS metaverilerini siler ve İndirilenler / Masaüstü klasörlerini önizleme + tek tıkla Geri Al (Undo) ile düzenler.
- **📈 Canlı CPU / RAM & Ağ Hızı Sparkline Monitörü:** Win32 `GetSystemTimes` + `NetworkInterface` üzerinden saniyede bir güncellenen gerçek zamanlı sparkline grafikler ve en çok bellek kullanan ilk 10 işlem tablosu.
- **⌨️ %100 Çalışma Zamanı Çift Dilli Arayüz (`57/57` Ekran + `CleanerRule` + Tepsi + Temalı Diyaloglar), `SystemAuto` Tema & Tam Erişilebilirlik:** `SystemAuto` (Windows Açık/Koyu modu ile canlı senkronize), `ObsidianNeon`, `MidnightViolet`, `EmeraldMatrix` ve `FluentLight` temalarıyla **57/57 modüler Lazy View**, tüm 20 varsayılan `CleanerRule` kuralı/risk etiketi, enjekte edilebilir tema uyumlu `IUserDialogService` / `ThemedDialogWindow` pencereleri (`TranslateDialogTitle`), Komut Paleti ve Sistem Tepsisi (`Boş` / `Free`) `tr-TR` ve `en-US` arasında anında çevrilir; çoklu monitör güvenli **Pencere Konum Belleği**, Toast bildirimleri, **`F1` Klavye Kısayolları Rehberi** (`Ctrl+K`, `Ctrl+F`, `Ctrl+,`, `Ctrl+1..6`, `F5`, `F1`, `Esc`), buton/checkbox `IsKeyboardFocused` odak halkaları ve **57/57 görünümde tam `AutomationProperties.Name`** ekran okuyucu etiketleri içerir.
- **📦 ReadyToRun Kurulum, Tedarik Zinciri Güvenliği & Açık Kaynak Standartları:** Merkezi Paket Yönetimi (`Directory.Packages.props` + `Microsoft.Data.Sqlite` `10.0.0`), `AnalysisLevel=latest-recommended` `.NET Analyzers`, `NuGetAudit`, CodeQL analizi, SLSA `attest-build-provenance`, `[LibraryImport]` + `DllImportSearchPath.System32` Win32 çağrıları, 600K PBKDF2 + bellek sıfırlamalı akışlı `FCVAULT2` şifreleme, `VACUUM` öncesi/sonrası SQLite `PRAGMA quick_check` bütünlük denetimi + yedek geri alma, `.bak` kurtarmalı `AtomicFileStorageService` (ayarlar, geçmiş, düzenleyici, snapshot, doluluk tahmini ve CLI/ayar dışa aktarma), arka plan `TickLiveTelemetryAsync`, tam `FluentLight` tema uyumu, `AsyncRelayCommand` + `IDisposable` temizliği, kilitlenmesiz birleşik `SafeProcessRunner`, junction/sistem yolu korumalı öğütücü, `[GeneratedRegex]`, global `DataGrid` geri dönüşümlü sanallaştırması, rotasyonlu/maskeli `ErrorLogService`, yerel `win-x64` & `win-arm64` dağıtımı, `installer/FreeCleaner.iss`, `PublishReadyToRun` derlemesi, otomatik hash'li `FreeCleaner-Winget-Manifests-*.zip`, `LICENSE` (MIT), `SECURITY.md`, `CONTRIBUTING.md`, **101 başarılı birim testi** (`%36.0` CI alt eşiğiyle `%39.41` satır / `%26.34` dal Cobertura kapsamı) ve CI `-p:TreatWarningsAsErrors=true` ile korunur.

### Tüm Modüller (6 Kategori • 57 Araç)

#### 1. Temizlik & Disk Kurtarma (12 Modül)
1. **Genel Bakış (Dashboard), Ömür Boyu Geçmiş & 100 Puanlık Sağlık Kontrolü**
2. **Etkileşimli Görsel Disk Haritası (Treemap + Akıllı Danışman & Güvenli Silme)**
3. **Dev Windows Sistem Alanı Kurtarıcısı (`hiberfil.sys`, `WinSxS`, `CompactOS`, `NVIDIA/AMD Installer`)**
4. **Benzer / Tekrarlayan Fotoğraf Bulucu (64-bit Perceptual `dHash` + Önizleme)**
5. **Akıllı Temizlik (Tümü)** — Simülasyon önizlemesi, tek tık profiller & Geri Dönüşüm Kutusu koruması
6. **Windows & Sistem Temizliği** — Temp, Prefetch, Windows Update, Dump & Geri Dönüşüm
7. **Web Tarayıcıları & Yerel AI Modelleri** — Chrome (`OptGuideOnDeviceModel`/`weights.bin`), Edge, Brave, Firefox
8. **Uygulama & Geliştirici Önbellekleri** — Discord, Steam, VS Code vb.
9. **Büyük Dosya & Kopya (`Duplicate`) Bulucu** — SHA-256 eşleştirme & Sıfır Veri Kayıplı NTFS Hardlink Birleştirme
10. **İndirilenler Asistanı & Boş Klasör Temizleyici**
11. **Bloatware (Windows 10/11 UWP Appx) Kaldırıcı**
12. **Askeri Standartta Dosya Öğütücü (`DoD 5220.22-M`) & Boş Alan Silici**

#### 2. Performans, Açılış & Donanım (9 Modül)
13. **Canlı CPU, RAM & Ağ Sparkline Monitörü**
14. **Başlangıç (`Startup`) Yöneticisi** (`HKCU`/`HKLM` + Başlangıç klasörü)
15. **Windows Açılış Süresi (`Boot Time`) Analizcisi**
16. **Canlı RAM (`EmptyWorkingSet`) & Ağ Hızlandırıcı**
17. **Canlı CPU Önceliği (`Process Priority`) & Verimlilik Modu (`EcoQoS`)**
18. **SSD / NVMe Canlı Okuma & Yazma Hız Testi (Benchmark)**
19. **Disk Sağlığı (`S.M.A.R.T.`), Pil Raporu & Nihai Performans Güç Planı**
20. **Derin Donanım Envanteri, BIOS & OEM Windows Lisans Anahtarı**
21. **Monitör EDID, Yenileme Hızı (`Hz`) & Ekran Kartı (GPU) Sıfırlama Doktoru**

#### 3. Sistem Onarım & Windows 11 (10 Modül)
22. **Winget Toplu Program Güncelleyici** (canlı paket ilerleme çubuğu & iptal desteği)
23. **Windows 11 İnce Ayar (Ultimate Tweaker)** — Klasik sağ tık, dosya uzantıları, Explorer hızlandırma
24. **Kayıt Defteri (Registry) Tarama, Otomatik `.reg` Yedekleme & Onarım**
25. **Program Kaldırıcı & Yetim (`Orphan`) `AppData` Kalıntı Temizleyici**
26. **Windows Servisleri & Telemetri Optimizasyon Yöneticisi**
27. **Sistem Onarım Merkezi (`SFC /scannow`, `DISM`, `FontCache`, `Windows.old`)**
28. **Sistem Geri Yükleme Noktası & Windows God Mode Merkezi**
29. **Sağ Tık Bağlam Menüsü (`Shell Context Menu`) Yöneticisi**
30. **Dosya Uzantısı & Varsayılan Program (`UserChoice`) Doktoru**
31. **Sistem & Kullanıcı Font (Yazı Tipi) Yöneticisi**

#### 4. Ağ, Wi-Fi & Güvenlik Duvarı (6 Modül)
32. **Yerel Ağ (`LAN / Wi-Fi`) Cihaz Radarı & ARP/Ping Tarayıcı**
33. **Kayıtlı Wi-Fi Şifre Görüntüleyici & Canlı DNS Ping Kıyaslayıcı**
34. **Canlı TCP/UDP Ağ Bağlantıları & `Hosts` Dosyası Yönetici**
35. **Ağ Adaptörü, IP/MAC & `TCP/IP Winsock` Onarım Doktoru**
36. **Windows Güvenlik Duvarı (`Firewall`) Denetçisi & Tek Tıkla Uygulama Kesici**
37. **Geliştirici Port Avcısı (`Port Killer` — 3000/5000/8080/PID)**

#### 5. Güvenlik, Gizlilik & Adli Analiz (10 Modül)
38. **Fotoğraf EXIF / GPS Gizlilik Temizleyici & Optimizer**
39. **Windows Defender Kalkanı & Gizli İstisna (`ExclusionPath`) Denetçisi**
40. **Süreç & Dijital İmza (`X.509` + `SHA-256`) Denetçisi**
41. **AES-256-GCM Dosya Şifreleme Kasası (`.fcvault`) & Çoklu Hash Doğrulayıcı**
42. **Windows Gizlilik & Telemetri Kalkanı (`MRU`, `JumpList`, `Activity History`)**
43. **Akıllı Pano (`Clipboard`) Kalkanı & Takip URL Temizleyici**
44. **BitLocker Durum Denetçisi & Bağlı `ISO / VHD` Sanal Disk Yöneticisi**
45. **Windows Kimlik Kasası (`cmdkey`) & `SSH / Git` Anahtar Denetçisi**
46. **USB Depolama Geçmişi (`USBSTOR` Adli Analiz) & İz Temizleyici**
47. **Mavi Ekran (`BSOD`), Çökme Dökümleri (`.dmp`) & `EventLog` Analizcisi**

#### 6. Geliştirici, AI & Verimlilik (10 Modül)
48. **Yerel Yapay Zeka (`AI / LLM`) Model & Önbellek Avcısı** (`Ollama`, `HuggingFace`, `LM Studio`)
49. **WSL2 Dağıtım Yöneticisi & Docker Konteyner / İmaj Temizleyici**
50. **Git Depoları (`.git`) Sıkıştırıcı (`git gc`) & Proje Durum Radarı**
51. **DevCleaner & Gamer** — Visual Studio `Debug/Retail/Release/obj/.vs`, `node_modules`, `.venv`, DirectX/NVIDIA/AMD Shader önbellekleri
52. **Küresel Paket Önbellekleri (`NuGet`, `NPM`, `Pip`, `Cargo`, `Gradle`) & `WSL2 / Claude VHDX` Sıkıştırıcı**
53. **Tarayıcı & IDE `SQLite` Veritabanı Sıkıştırıcı (`VACUUM`)**
54. **Toplu Dosya Yeniden Adlandırıcı (`PowerRename` & Geri Alma)**
55. **Kilitli Dosya Çözücü (`Win32 Restart Manager`)**
56. **Ortam Değişkeni (`PATH`) Doktoru & Kırık Kısayol (`.lnk`) Onarıcı**
57. **Zamanlanmış Görevler (`Task Scheduler`), Tarayıcı Eklentileri, `KB`/`DriverStore`, Beyaz Liste & HTML/JSON Raporlama**

### CLI Parametreleri & Klavye Kısayolları

| CLI Parametresi | İşlev |
| :--- | :--- |
| `--treemap "<yol>"` | Belirtilen klasör için Disk Treemap taraması başlatır |
| `--exif "<yol>"` | Belirtilen klasördeki fotoğrafları EXIF/GPS taramasından geçirir |
| `--similar "<yol>"` | Belirtilen klasörde Benzer Fotoğraf (`dHash`) taraması başlatır |
| `--deep-space` | Dev Windows Sistem Alanı Kurtarıcısı'nı açıp analizi başlatır |
| `--health-check` | Dashboard üzerinde 100 puanlık Sistem Sağlık Kontrolü başlatır |
| `--scheduled-maintenance` | Zamanlanmış otomatik bakımı çalıştırıp tamamlanınca kapatır |
| `--dry-run` | Hiçbir dosya silmeden kuralları güvenli şekilde analiz eder |
| `--profile <SafeDaily\|DeveloperDeep\|GamerBoost\|MaxPrivacy\|OzelAd>` | Açılışta yerleşik veya kullanıcı tanımlı temizlik profilini uygular |
| `--report-json "<yol>"` | Tarama özetini yapılandırılmış JSON (`CliAutomationReport`) olarak yazar |
| `--exit` / `--headless` | CLI otomasyonu bitince uygulamayı otomatik kapatır |
| `--minimized` / `--tray` | Pencere göstermeden doğrudan Sistem Tepsisinde sessiz başlatır |

| Kısayol | İşlev |
| :--- | :--- |
| `Ctrl + K` | Hızlı Komut & Modül Paletini Aç / Kapat |
| `Ctrl + F` | Sol Menü Modül Arama Kutusuna Odaklan |
| `Ctrl + ,` | Ayarlar Ekranını Aç (Beyaz Liste, Tema, Dil, Güncelleme) |
| `Ctrl + 1` .. `Ctrl + 6` | Hızlı Geçiş: Dashboard / Temizleyici / Treemap / Canlı İzle / Winget / Ayarlar |
| `F5` | Aktif Modülü Yenile / Yeniden Tara |
| `↑` / `↓` + `Enter` | Komut Paletinde Modül veya Profil Seç ve Çalıştır |
| `Esc` | İlk Kurulum Ekranını veya Komut Paletini Kapat |

### Kurulum, Derleme & Taşınabilir Sürüm (`.exe`)

#### Gereksinimler
- **.NET 10 SDK** (`net10.0-windows`)
- **Windows 10 / Windows 11 (x64 & ARM64)**

#### Komut Satırı ile Derleme & Test (89 Test + Kapsam)
```powershell
dotnet restore FreeCleaner.slnx
dotnet build FreeCleaner.slnx --configuration Release --no-restore -p:TreatWarningsAsErrors=true
dotnet test FreeCleaner.Tests/FreeCleaner.Tests.csproj --configuration Release --no-build --collect:"XPlat Code Coverage"
dotnet run --project FreeCleaner/FreeCleaner.csproj
```

#### Arayüz Testi (Smoke Test)
```powershell
.\scripts\smoke-test.ps1 -Screenshots   # tüm bölümleri TR + EN gezer; rapor %TEMP%\FreeCleaner-smoke içinde
```
Hiçbir şey silinmez: diyaloglar otomatik "Hayır"/"İptal" ile yanıtlanır, ayarlar ve günlükler geçici bir veri klasörüne yazılır.

#### Tek Dosya Taşınabilir `.exe` (`win-x64` / `win-arm64`) & Windows Setup Kurulumu
```powershell
.\scripts\Publish-Portable.ps1
# İsteğe bağlı ARM64 tek dosya derleme, ReadyToRun x64, USB taşınabilir + Winget manifest önizlemesi:
.\scripts\Publish-Portable.ps1 -Runtime win-arm64
.\scripts\Publish-Portable.ps1 -ReadyToRun -IncludePortableFlag -GenerateWingetManifests
# İsteğe bağlı Inno Setup 6 Windows Kurulum Dosyası (dist/FreeCleaner-Setup-win-x64-v4.8.0.exe):
ISCC.exe /DMyAppVersion=4.8.0 /DSourceExe=..\artifacts\portable\FreeCleaner.exe installer\FreeCleaner.iss
```
Çıktı dosyası `artifacts/portable/FreeCleaner.exe` (veya `artifacts/portable-arm64/FreeCleaner.exe`) konumunda oluşturulur. GitHub üzerinde `v*` (örneğin `git tag v4.8.0 && git push origin v4.8.0`) etiketi gönderildiğinde `.github/workflows/release.yml` otomatik olarak testleri çalıştırıp `PublishReadyToRun` x64 + yerel ARM64 derlemeleri ve SLSA attestation ile Standart Taşınabilir `.zip` (`win-x64` & `win-arm64`), USB (`portable.flag`) `.zip`, Windows Setup `.exe`, gerçek hash'li `FreeCleaner-Winget-Manifests-*.zip`, `FreeCleaner.exe` ve `SHA256SUMS.txt` dosyasını GitHub Releases sayfasında yayınlar.

### Kod İmzalama Politikası (Code signing policy)

Ücretsiz kod imzalama [SignPath.io](https://signpath.io) tarafından, sertifika [SignPath Foundation](https://signpath.org) tarafından sağlanmaktadır (başvuru değerlendirmede; onaylanana kadar sürümler imzasızdır ve `SHA256SUMS.txt` ile SLSA attestation üzerinden doğrulanır).
Roller: commit, inceleme ve imzalama onayı — [Erkan Bahar (@erkanbahar)](https://github.com/erkanbahar). Ayrıntılar: [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md).
Gizlilik: telemetri yoktur; tek otomatik ağ isteği kapatılabilen günlük güncelleme kontrolüdür — bkz. [PRIVACY.md](PRIVACY.md).
