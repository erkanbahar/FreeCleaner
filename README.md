# FreeCleaner Pro v4.4 (.NET 10 WPF)

[![FreeCleaner .NET 10 CI](https://github.com/erkanbahar/FreeCleaner/actions/workflows/dotnet-ci.yml/badge.svg)](https://github.com/erkanbahar/FreeCleaner/actions/workflows/dotnet-ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10.0--windows-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/Windows-10%20%7C%2011%20(x64)-0078D6?logo=windows)
![Modules](https://img.shields.io/badge/Modules-57%20Tools-10B981)
![Tests](https://img.shields.io/badge/Tests-64%20Passing-22C55E)
![Language](https://img.shields.io/badge/Language-English%20%7C%20T%C3%BCrk%C3%A7e-38BDF8)

> 🌐 **Language / Dil:** [English](#english) • [Türkçe](#türkçe) • 📋 [Changelog](CHANGELOG.md)

---

## English

**FreeCleaner Pro** is a comprehensive, **57-module** Windows system maintenance, disk recovery, performance optimization, security/privacy, and developer utility suite built with **.NET 10 (`net10.0-windows`)** and **WPF (Modern Fluent UI)** — with **zero third-party UI dependencies**.

### Flagship & Power-User Features

- **🔁 Single-Instance & Named Pipe IPC Bridge (`Mutex` + `NamedPipeServerStream`):** Prevents duplicate tray/window launches; secondary invocations (including Windows Explorer right-click actions) forward CLI arguments immediately to the running primary window and restore it to front.
- **🖱️ Windows Explorer Right-Click Menu (`HKCU`), Silent Tray Auto-Start (`--minimized`) & CLI:** Install/remove cascading Explorer context-menu shortcuts under `HKCU` without Administrator rights, or enable Windows startup directly to System Tray via `--minimized`.
- **🛡️ Built-In & User-Defined Cleaning Profiles + First-Run Setup Wizard:** Instantly apply **Safe Daily**, **Developer Deep**, **Gamer Boost**, or **Max Privacy** presets, save/apply/delete your own **Custom Cleaning Profiles** (`CustomCleaningProfile`) from Smart Cleaner, `Ctrl+K`, or CLI (`--profile <CustomName>`), and configure Language, Theme, Recycle Bin safety, and Portable Mode on first launch (re-openable from Settings) alongside portable `.json` settings backup/restore.
- **🔗 Zero-Data-Loss NTFS Hardlink Deduplicator:** Merges byte-for-byte identical duplicate files (`CreateHardLinkW` + `GetFileInformationByHandle` inode checks + full SHA-256 verification + same-NTFS-volume safety guard + atomic swap/rollback) so disk space is reclaimed while keeping every file path accessible.
- **📈 Disk Snapshot Diff ("Where Did My Disk Space Go?"):** Saves lightweight Treemap snapshots (`%LocalAppData%\FreeCleaner\snapshots`) and computes per-folder `+GB` growth deltas (`ComputeGrowthDiff`) between scans.
- **🔒 Smart Browser / VM Process Lock Guard:** Detects active browsers (`Chrome`, `Edge`, `Brave`, `Firefox`, `Discord`, `Steam`, `VS Code`) before cleanup and `Claude Desktop` / `Docker` / `WSL2` before `.vhdx` compaction, offering a safe **Close & Continue / Skip / Cancel** prompt.
- **⬇️ In-App GitHub Releases Updater with SHA-256 Verification:** Downloads portable `.exe`/`.zip` releases with live progress tracking, validates cryptographic `SHA-256` digests (`SHA256SUMS.txt` / GitHub asset digest), and runs an atomic backup/swap/relaunch script.
- **🔔 Live System Tray Telemetry, Cooldown & Proactive Storage Forecast Alerts:** Displays real-time `CPU %`, `RAM %`, and `C:` free GB in the tray tooltip (63-char safe), supports minimize-to-tray, sends cooldown-protected critical alerts (`>= 90%` Disk / `>= 92%` RAM), and notifies proactively when predicted days to 90% disk usage fall below your configurable threshold (`7 / 15 / 30` days — click balloon to open Smart Cleaner).
- **🛡️ Global Crash Guard (`CrashGuardService`):** Captures unhandled UI/AppDomain/Task exceptions cleanly, writes redacted `crashes/crash-*.log` reports (`%USERPROFILE%` + secret sanitization), and offers one-click redacted Desktop Support Bundle `.zip` export (`latest_crash_sanitized.log` included).
- **📊 SVG Diagnostic HTML Report & Filterable Activity Console:** Exports rich HTML/JSON reports featuring an inline SVG health-score gauge and 14-day cleanup history bars, paired with live log search, clipboard copy, `.log` export, and clear buttons in the Activity Console.
- **🪟 Native Windows 11 DWM & Manifest Polish:** Includes `app.manifest` (`PerMonitorV2` DPI awareness and `longPathAware` for deep `node_modules`/cache paths) plus DWM rounded corners and theme-adaptive frame attributes.
- **🎒 True Portable Mode (`portable.flag`) & Diagnostic Support Bundle (`.zip`):** Stores settings, cleanup history, snapshots, telemetry, and logs inside a local `data\` folder when enabled, and exports a redacted support `.zip` (`diagnostic_report.html`, `system_summary.json`, sanitized logs).
- **🩺 Dashboard, Lifetime Saved Space, Storage Fill Forecast & 100-Point Health Check:** Scores disk usage, junk load, startup items, and RAM; tracks **Lifetime Reclaimed Space**, a 14-day cleanup chart, and linear `C:` storage growth forecast (`+GB/day` & days to 90%).
- **🗺️ Interactive Disk Treemap & Smart File Advisor:** Fast Win32 `FileSystemEnumerator` scanning visualizes large folders in seconds and classifies 30+ known targets (`OptGuideOnDeviceModel`, `Claude-Data rootfs.vhdx`, `Docker VHDX`, `hiberfil.sys`, `node_modules`) with safe recycle or protection guidance.
- **💾 Deep Windows Space Recovery & VHDX Compactor:** Manages `hiberfil.sys` (`powercfg`), `WinSxS` (`DISM`), lossless `CompactOS`, `NVIDIA/AMD` installer caches, and one-click DiskPart `compact vdisk` for WSL2 / Docker / Claude VM `.vhdx` virtual disks.
- **🖼️ Perceptual Similar Photo Finder (`dHash`), 📸 EXIF Scrubber & 🗂️ Smart Folder Organizer:** Clusters burst photos with 64-bit `dHash`, strips GPS/EXIF metadata pixel-safely, and organizes Downloads / Desktop folders with one-click Undo.
- **📈 Live CPU / RAM & Network Sparkline Monitor:** 1-second Win32 `GetSystemTimes` + `NetworkInterface` telemetry with sparkline charts and top-10 memory consumers.
- **⌨️ 100% Bilingual 57/57 Views, Dynamic System Tray & Power-User Shortcuts:** Live switching across `ObsidianNeon`, `MidnightViolet`, `EmeraldMatrix`, and `FluentLight` themes plus full `en-US` / `tr-TR` translation across **all 57/57 Lazy Views**, sidebar modules, `Ctrl+K` Command Palette, and System Tray menu/tooltip (`Free` vs `Boş`). Includes multi-monitor-clamped **Window Placement Memory** (`WindowPlacementService`), keyboard shortcuts (`Ctrl+K`, `Ctrl+F`, `Ctrl+,`, `Ctrl+1..6`, `F5`, `Esc`), and `AutomationProperties.Name` accessibility labels.
- **📦 Windows Setup Installer (`Inno Setup 6`), Winget Manifests & Modular Models Architecture:** Every module view instantiates on demand via `LazyViewRegistry` (zero inline view blocks in `MainWindow.xaml`), paired with modular domain models (`SystemAndHardwareModels`, `SecurityAndPrivacyModels`, `NetworkAndStorageModels`, `DeveloperAndMediaModels`), `installer/FreeCleaner.iss`, `installer/winget/` (`1.6.0` templates), `64` passing unit tests (`29.62%` line / `20.91%` branch Cobertura coverage), and CI `-p:TreatWarningsAsErrors=true`.

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
- **Windows 10 / Windows 11 (x64)**

#### CLI Build & Unit Tests (64 Tests + Coverage)
```powershell
dotnet restore FreeCleaner.slnx
dotnet build FreeCleaner.slnx --configuration Release --no-restore -p:TreatWarningsAsErrors=true
dotnet test FreeCleaner.Tests/FreeCleaner.Tests.csproj --configuration Release --no-build --collect:"XPlat Code Coverage"
dotnet run --project FreeCleaner/FreeCleaner.csproj
```

#### Single-File Portable `.exe` & Windows Installer (`Setup .exe`)
```powershell
.\scripts\Publish-Portable.ps1
# Optional pre-flagged USB portable output:
.\scripts\Publish-Portable.ps1 -IncludePortableFlag
# Optional Inno Setup 6 Windows Installer (dist/FreeCleaner-Setup-win-x64-v4.4.0.exe):
ISCC.exe /DMyAppVersion=4.4.0 /DSourceExe=..\artifacts\portable\FreeCleaner.exe installer\FreeCleaner.iss
```
Outputs `artifacts/portable/FreeCleaner.exe`. Pushing a `v*` tag (e.g., `git tag v4.4.0 && git push origin v4.4.0`) triggers `.github/workflows/release.yml` to verify tests, build with warnings-as-errors, and publish Standard Portable `.zip`, USB (`portable.flag`) `.zip`, Windows Setup `.exe` installer, `FreeCleaner.exe`, and `SHA256SUMS.txt` on GitHub Releases (`installer/winget/` contains Winget `1.6.0` manifest templates).

---

## Türkçe

[⬆️ English](#english)

**FreeCleaner Pro**, **.NET 10 (`net10.0-windows`)** ve **WPF (Modern Fluent UI)** altyapısıyla geliştirilmiş, sıfır üçüncü taraf UI bağımlılığına sahip, **57 modüllü** kapsamlı bir Windows sistem temizleme, disk kurtarma, performans optimizasyonu, güvenlik/gizlilik ve geliştirici bakım paketidir.

### Öne Çıkan Amiral Gemisi Özellikler

- **🔁 Tek Örnek (Single-Instance) & Named Pipe IPC Köprüsü:** Uygulama sistem tepsisinde açıkken ikinci bir kopya açılmasını önler; Gezgin sağ tık veya CLI üzerinden gelen parametreleri mevcut pencereye iletip uygulamayı anında öne getirir.
- **🖱️ Windows Gezgini Sağ Tık Menüsü (`HKCU`), Sessiz Başlatma (`--minimized`) & CLI:** Yönetici izni olmadan `HKCU` altında sağ tık menüsü kurup kaldırır veya Windows başlangıcında `--minimized` ile doğrudan sistem tepsisine gizli başlatır.
- **🛡️ Hazır & Kullanıcı Tanımlı Özel Temizlik Profilleri + İlk Kurulum Sihirbazı:** **Güvenli Günlük**, **Geliştirici**, **Oyuncu** ve **Maksimum Gizlilik** önayarlarının yanı sıra Akıllı Temizlik içinden kendi **Özel Temizlik Profillerinizi** (`CustomCleaningProfile`) kaydedin, uygulayın, silin veya `Ctrl+K` / `--profile <OzelAd>` ile çalıştırın; ilk açılışta Dil, Tema, Geri Dönüşüm ve Taşınabilir Mod kurulumunu tamamlayın (Ayarlar'dan yeniden açılabilir) ve dışlama listesi, kural seçimleri, tema, dil ve sabitlenen modülleri `.json` ile yedekleyin.
- **🔗 Sıfır Veri Kayıplı NTFS Hardlink Birleştirici:** Aynı NTFS sürücüsündeki birebir kopya dosyaları (`CreateHardLinkW` + `GetFileInformationByHandle` inode kontrolü + tam SHA-256 doğrulaması + atomik geri alma koruması) tek fiziksel bloğa bağlayarak disk alanı açarken tüm dosya yollarını yerinde korur.
- **📈 Klasör Boyutu Anlık Görüntü Karşılaştırması (Disk Snapshot Diff):** Treemap taramalarını hafif anlık görüntüler (`%LocalAppData%\FreeCleaner\snapshots`) olarak kaydeder; önceki taramaya göre en çok büyüyen klasörleri (`+GB` artış) listeler.
- **🔒 Akıllı Süreç Kilit Koruması (Process Lock Guard):** Temizlik öncesinde açık tarayıcı/uygulamaları (`Chrome`, `Edge`, `Brave`, `Firefox`, `Discord`, `Steam`, `VS Code`), `.vhdx` sıkıştırma öncesinde ise `Claude Desktop` / `Docker` / `WSL2` süreçlerini algılayıp güvenli kapatma seçeneği sunar.
- **⬇️ Uygulama İçi SHA-256 Doğrulamalı Güncelleyici:** GitHub Releases üzerinden `.exe`/`.zip` paketlerini ilerleme çubuğuyla indirir, kriptografik `SHA-256` bütünlüğünü (`SHA256SUMS.txt` / asset digest) doğrular ve atomik yedekleme/yeniden başlatma uygular.
- **🔔 Canlı Sistem Tepsisi Telemetrisi, Bekleme Süreli & Proaktif Depolama Tahmin Uyarıları:** Tepsi ikonu üzerine gelince anlık `CPU %`, `RAM %` ve `C:` boş alanı (63 karakter sınırına uygun) gösterir; Disk (`>= %90`) veya RAM (`>= %92`) kritik eşiğe ulaştığında 30 dakikalık cooldown ile uyarır; disk doluluk tahmini belirlenen kalan gün eşiğinin (`7 / 15 / 30` gün) altına düştüğünde proaktif bildirim üretir (bildirime tıklayınca Akıllı Temizlik açılır).
- **🛡️ Global Çökme Koruması (`CrashGuardService`):** Beklenmedik UI, `AppDomain` ve arka plan `Task` hatalarını yakalar; `%USERPROFILE%` ve gizli anahtarlardan arındırılmış dökümleri `crashes/crash-*.log` altına yazar (en fazla 20 log saklar) ve Masaüstüne `latest_crash_sanitized.log` içeren Teşhis Destek Paketi `.zip` oluşturulmasını önerir.
- **📊 SVG Sağlık Göstergeli HTML Rapor & Filtrelenebilir Konsol:** Inline SVG sağlık puanı kadranı ve 14 günlük geçmiş çubukları içeren HTML/JSON rapor üretir; işlem konsolunda canlı arama, panoya kopyalama, `.log` dışa aktarma ve temizleme araçları sunar.
- **🪟 Windows 11 Yerel Pencere Cilası & `app.manifest`:** `PerMonitorV2` DPI, `longPathAware` uzun yol desteği ve DWM yuvarlatılmış pencere/uyumlu çerçeve özellikleri içerir.
- **🎒 Gerçek Taşınabilir Mod (`portable.flag`) & Teşhis Destek Paketi (`.zip`):** Etkinleştirildiğinde tüm ayar, geçmiş, snapshot, telemetri ve logları uygulama yanındaki `data\` klasöründe tutar; hassas verilerden arındırılmış tek dosyalık destek `.zip` arşivi dışa aktarır.
- **🩺 Genel Bakış (Dashboard), Ömür Boyu Kazanılan Alan, Doluluk Tahmini & 100 Puanlık Sağlık Kontrolü:** Disk doluluğu, çöp yükü, başlangıç uygulamaları ve RAM'i puanlar; **Ömür Boyu Kurtarılan Alan**, 14 günlük grafik ve `C:` için doğrusal dolma tahminini (`+GB/gün` & `%90`'a kalan gün) gösterir.
- **🗺️ Etkileşimli Görsel Disk Haritası (Treemap + Akıllı Dosya Danışmanı):** Doğrudan Win32 `FileSystemEnumerator` ve 2 seviyeli paralel taramayla dev klasörleri saniyeler içinde görselleştirir; `OptGuideOnDeviceModel`, `Claude-Data rootfs.vhdx`, `Docker VHDX`, `hiberfil.sys`, `node_modules` gibi 30+ bilinen yapıyı tanıyıp **Güvenle Geri Dönüşüm'e Gönder** veya koruma önerisi sunar.
- **💾 Dev Windows Sistem Alanı Kurtarıcısı & VHDX Sıkıştırıcı:** Hazırda bekletmeyi kapatma/yarıya indirme (`powercfg`), bileşen deposu temizliği (`DISM`), kayıpsız `CompactOS` sıkıştırma, `NVIDIA/AMD` sürücü artıkları ve WSL2/Docker/Claude VM `.vhdx` sanal diskleri için tek tıkla DiskPart `compact vdisk` desteği sunar.
- **🖼️ Benzer Fotoğraf Bulucu (`dHash`), 📸 EXIF Temizleyici & 🗂️ Akıllı Klasör Düzenleyici:** 64-bit `dHash` ile seri fotoğrafları kümeleyip en kalitelisini korur, EXIF/GPS metaverilerini siler ve İndirilenler / Masaüstü klasörlerini önizleme + tek tıkla Geri Al (Undo) ile düzenler.
- **📈 Canlı CPU / RAM & Ağ Hızı Sparkline Monitörü:** Win32 `GetSystemTimes` + `NetworkInterface` üzerinden saniyede bir güncellenen gerçek zamanlı sparkline grafikler ve en çok bellek kullanan ilk 10 işlem tablosu.
- **⌨️ 57/57 Görünümlerde %100 Çift Dilli Arayüz, Dinamik Sistem Tepsisi & Klavye Kısayolları:** `ObsidianNeon`, `MidnightViolet`, `EmeraldMatrix` ve `FluentLight` temaları ile tüm sol menü, Komut Paleti, **57/57 modüler Lazy View** ve Sistem Tepsisi menüsü/ipucu (`Boş` / `Free`) `tr-TR` ve `en-US` arasında anında çevrilir. Çoklu monitör sınırlarına güvenli **Pencere Konum Belleği** (`WindowPlacementService`), klavye kısayolları (`Ctrl+K`, `Ctrl+F`, `Ctrl+,`, `Ctrl+1..6`, `F5`, `Esc`) ve `AutomationProperties.Name` erişilebilirlik etiketleri içerir.
- **📦 Windows Setup Kurulumu (`Inno Setup 6`), Winget Manifestleri & Modüler Mimari:** Tüm ekranlar `LazyViewRegistry` ile talep üzerine yüklenir (`MainWindow.xaml` içinde satır içi görünüm kalmamıştır); ViewModel parçaları ve alan bazlı model dosyaları (`SystemAndHardwareModels`, `SecurityAndPrivacyModels`, `NetworkAndStorageModels`, `DeveloperAndMediaModels`), `installer/FreeCleaner.iss`, `installer/winget/` (`1.6.0` manifestleri), **64 başarılı birim testi** (`%29.62` satır / `%20.91` dal Cobertura kapsamı) ve CI `-p:TreatWarningsAsErrors=true` ile korunur.

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
- **Windows 10 / Windows 11 (x64)**

#### Komut Satırı ile Derleme & Test (64 Test + Kapsam)
```powershell
dotnet restore FreeCleaner.slnx
dotnet build FreeCleaner.slnx --configuration Release --no-restore -p:TreatWarningsAsErrors=true
dotnet test FreeCleaner.Tests/FreeCleaner.Tests.csproj --configuration Release --no-build --collect:"XPlat Code Coverage"
dotnet run --project FreeCleaner/FreeCleaner.csproj
```

#### Tek Dosya Taşınabilir `.exe` & Windows Setup Kurulumu
```powershell
.\scripts\Publish-Portable.ps1
# İsteğe bağlı hazır USB taşınabilir mod (portable.flag) üretimi:
.\scripts\Publish-Portable.ps1 -IncludePortableFlag
# İsteğe bağlı Inno Setup 6 Windows Kurulum Dosyası (dist/FreeCleaner-Setup-win-x64-v4.4.0.exe):
ISCC.exe /DMyAppVersion=4.4.0 /DSourceExe=..\artifacts\portable\FreeCleaner.exe installer\FreeCleaner.iss
```
Çıktı dosyası `artifacts/portable/FreeCleaner.exe` konumunda oluşturulur. GitHub üzerinde `v*` (örneğin `git tag v4.4.0 && git push origin v4.4.0`) etiketi gönderildiğinde `.github/workflows/release.yml` otomatik olarak testleri çalıştırıp Standart Taşınabilir `.zip`, USB (`portable.flag`) `.zip`, Windows Setup `.exe` kurulum paketi, `FreeCleaner.exe` ve `SHA256SUMS.txt` dosyasını GitHub Releases sayfasında yayınlar (`installer/winget/` klasöründe Winget `1.6.0` manifest şablonları bulunur).
