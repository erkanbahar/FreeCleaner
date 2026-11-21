# FreeCleaner Pro v4.0 (.NET 10 WPF)

[![FreeCleaner .NET 10 CI](https://github.com/erkanbahar/FreeCleaner/actions/workflows/dotnet-ci.yml/badge.svg)](https://github.com/erkanbahar/FreeCleaner/actions/workflows/dotnet-ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10.0--windows-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/Windows-10%20%7C%2011%20(x64)-0078D6?logo=windows)
![Modules](https://img.shields.io/badge/Mod%C3%BCller-55%20Ara%C3%A7-10B981)
![Language](https://img.shields.io/badge/Dil-T%C3%BCrk%C3%A7e%20%7C%20English-38BDF8)

**FreeCleaner Pro**, **.NET 10 (`net10.0-windows`)** ve **WPF (Modern Fluent UI)** altyapısıyla geliştirilmiş, sıfır üçüncü taraf UI bağımlılığına sahip, **55 modüllü** kapsamlı bir Windows sistem temizleme, performans optimizasyonu, güvenlik/gizlilik ve geliştirici bakım paketidir.

---

## Flagship Özellikler

- **🩺 Genel Bakış (Dashboard) & Tek Tıkla 100 Puanlık PC Sağlık Kontrolü:** Disk doluluğu, birikmiş çöp miktarı, başlangıç yükü ve RAM kullanımını puanlar; tek tıkla analiz + RAM Working Set boşaltma + DNS yenileme uygular.
- **🗺️ Etkileşimli Görsel Disk Haritası (Treemap Visualizer):** Klasör ve dosyaların kapladığı alanı orantılı renkli kutucuklarla görselleştirir; klasör bloklarına tıklayarak içine inebilir (`Drill Down`), üst klasöre çıkabilir ve Gezgin'de açabilirsiniz.
- **📸 Fotoğraf EXIF / GPS Gizlilik Temizleyici (Metadata Scrubber):** JPEG, PNG ve TIFF fotoğraflardaki gömülü GPS konum koordinatlarını, kamera/cihaz bilgisini ve yazılım meta verilerini **orijinal yönelimi ve piksel kalitesini bozmadan** temizler (`FreeCleaner_PhotoBackup` isteğe bağlı yedekli + akıllı JPEG sıkıştırma desteği).
- **📈 Canlı CPU / RAM & Ağ Hızı Sparkline Monitörü:** Win32 `GetSystemTimes` + `NetworkInterface` üzerinden saniyede bir güncellenen gerçek zamanlı sparkline grafikler ve en çok bellek kullanan ilk 10 işlem tablosu.
- **⌨️ `Ctrl+K` Hızlı Komut Paleti & Sabitlenen Favoriler:** 55 modülün tamamına ve hızlı eylemlere (`RAM Boşalt`, `DNS Temizle`, `Sağlık Kontrolü`, `Güncelleme Denetle`) klavyeden anında ulaşım.
- **🎨 4 Canlı Tema + Canlı Türkçe / English Dil Değiştirici:** `ObsidianNeon`, `MidnightViolet`, `EmeraldMatrix` ve `FluentLight` temaları ile `tr-TR` / `en-US` arayüz arasında yeniden başlatma gerektirmeden anlık geçiş.
- **🚀 Modüler Lazy View Mimarisi (`LazyViewRegistry`):** Ağır görünümler (`DashboardView`, `LiveMonitorView`, `DiskTreemapView`, `ExifScrubberView`) yalnızca ilk ziyaret edildiğinde yüklenerek açılış süresini ve başlangıç bellek tüketimini optimize eder.
- **🔔 Gerçek Windows Sistem Tepsisi (System Tray) & Zamanlanmış Bakım:** Pencereyi tepsiye küçültme, sağ tık hızlı menüleri ve otomatik eşik uyarıları.
- **🔄 GitHub Releases Güncelleme Kontrolcüsü:** Ayarlar sekmesinden tek tıkla GitHub Releases API üzerinden yeni taşınabilir `.exe` sürümünü denetleme.

---

## Tüm Modüller (6 Kategori • 55 Araç)

### 1. Temizlik & Disk Kurtarma (10 Modül)
1. **Genel Bakış (Dashboard) & 100 Puanlık Sağlık Kontrolü**
2. **Etkileşimli Görsel Disk Haritası (Treemap Visualizer)**
3. **Akıllı Temizlik (Tümü)** — Simülasyon önizlemesi & Geri Dönüşüm Kutusu koruması
4. **Windows & Sistem Temizliği** — Temp, Prefetch, Windows Update, Dump & Geri Dönüşüm
5. **Web Tarayıcıları** — Chrome, Edge, Brave, Firefox önbellek & çerez temizliği
6. **Uygulama & Geliştirici Önbellekleri** — Discord, Steam, VS Code vb.
7. **Büyük Dosya & Kopya (Duplicate) Bulucu** — SHA-256 tabanlı birebir eşleşme
8. **İndirilenler Asistanı & Boş Klasör Temizleyici**
9. **Bloatware (Windows 10/11 UWP Appx) Kaldırıcı**
10. **Askeri Standartta Dosya Öğütücü (`DoD 5220.22-M`) & Boş Alan Silici**

### 2. Performans, Açılış & Donanım (9 Modül)
11. **Canlı CPU, RAM & Ağ Sparkline Monitörü**
12. **Başlangıç (`Startup`) Yöneticisi** (`HKCU`/`HKLM` + Başlangıç klasörü)
13. **Windows Açılış Süresi (`Boot Time`) Analizcisi**
14. **Canlı RAM (`EmptyWorkingSet`) & Ağ Hızlandırıcı**
15. **Canlı CPU Önceliği (`Process Priority`) & Verimlilik Modu (`EcoQoS`)**
16. **SSD / NVMe Canlı Okuma & Yazma Hız Testi (Benchmark)**
17. **Disk Sağlığı (`S.M.A.R.T.`), Pil Raporu & Nihai Performans Güç Planı**
18. **Derin Donanım Envanteri, BIOS & OEM Windows Lisans Anahtarı**
19. **Monitör EDID, Yenileme Hızı (`Hz`) & Ekran Kartı (GPU) Sıfırlama Doktoru**

### 3. Sistem Onarım & Windows 11 (10 Modül)
20. **Winget Toplu Program Güncelleyici** (canlı paket ilerleme çubuğu & iptal desteği)
21. **Windows 11 İnce Ayar (Ultimate Tweaker)** — Klasik sağ tık, dosya uzantıları, Explorer hızlandırma
22. **Kayıt Defteri (Registry) Tarama, Otomatik `.reg` Yedekleme & Onarım**
23. **Program Kaldırıcı & Yetim (`Orphan`) `AppData` Kalıntı Temizleyici**
24. **Windows Servisleri & Telemetri Optimizasyon Yöneticisi**
25. **Sistem Onarım Merkezi (`SFC /scannow`, `DISM`, `FontCache`, `Windows.old`)**
26. **Sistem Geri Yükleme Noktası & Windows God Mode Merkezi**
27. **Sağ Tık Bağlam Menüsü (`Shell Context Menu`) Yöneticisi**
28. **Dosya Uzantısı & Varsayılan Program (`UserChoice`) Doktoru**
29. **Sistem & Kullanıcı Font (Yazı Tipi) Yöneticisi**

### 4. Ağ, Wi-Fi & Güvenlik Duvarı (6 Modül)
30. **Yerel Ağ (`LAN / Wi-Fi`) Cihaz Radarı & ARP/Ping Tarayıcı**
31. **Kayıtlı Wi-Fi Şifre Görüntüleyici & Canlı DNS Ping Kıyaslayıcı**
32. **Canlı TCP/UDP Ağ Bağlantıları & `Hosts` Dosyası Yönetici**
33. **Ağ Adaptörü, IP/MAC & `TCP/IP Winsock` Onarım Doktoru**
34. **Windows Güvenlik Duvarı (`Firewall`) Denetçisi & Tek Tıkla Uygulama Kesici**
35. **Geliştirici Port Avcısı (`Port Killer` — 3000/5000/8080/PID)**

### 5. Güvenlik, Gizlilik & Adli Analiz (10 Modül)
36. **Fotoğraf EXIF / GPS Gizlilik Temizleyici & Optimizer**
37. **Windows Defender Kalkanı & Gizli İstisna (`ExclusionPath`) Denetçisi**
38. **Süreç & Dijital İmza (`X.509` + `SHA-256`) Denetçisi**
39. **AES-256-GCM Dosya Şifreleme Kasası (`.fcvault`) & Çoklu Hash Doğrulayıcı**
40. **Windows Gizlilik & Telemetri Kalkanı (`MRU`, `JumpList`, `Activity History`)**
41. **Akıllı Pano (`Clipboard`) Kalkanı & Takip URL Temizleyici**
42. **BitLocker Durum Denetçisi & Bağlı `ISO / VHD` Sanal Disk Yöneticisi**
43. **Windows Kimlik Kasası (`cmdkey`) & `SSH / Git` Anahtar Denetçisi**
44. **USB Depolama Geçmişi (`USBSTOR` Adli Analiz) & İz Temizleyici**
45. **Mavi Ekran (`BSOD`), Çökme Dökümleri (`.dmp`) & `EventLog` Analizcisi**

### 6. Geliştirici, AI & Verimlilik (13 Modül)
46. **Yerel Yapay Zeka (`AI / LLM`) Model & Önbellek Avcısı** (`Ollama`, `HuggingFace`, `LM Studio`)
47. **WSL2 Dağıtım Yöneticisi & Docker Konteyner / İmaj Temizleyici**
48. **Git Depoları (`.git`) Sıkıştırıcı (`git gc`) & Proje Durum Radarı**
49. **DevCleaner & Gamer** — Visual Studio `Debug/Retail/Release/obj/.vs`, `node_modules`, `.venv`, DirectX/NVIDIA/AMD Shader önbellekleri
50. **Küresel Paket Önbellekleri (`NuGet`, `NPM`, `Pip`, `Cargo`, `Gradle`) & `WSL2 VHDX`**
51. **Tarayıcı & IDE `SQLite` Veritabanı Sıkıştırıcı (`VACUUM`)**
52. **Toplu Dosya Yeniden Adlandırıcı (`PowerRename` & Geri Alma)**
53. **Kilitli Dosya Çözücü (`Win32 Restart Manager`)**
54. **Ortam Değişkeni (`PATH`) Doktoru & Kırık Kısayol (`.lnk`) Onarıcı**
55. **Zamanlanmış Görevler (`Task Scheduler`), Tarayıcı Eklentileri, `KB`/`DriverStore`, Beyaz Liste & HTML/JSON Raporlama**

---

## Klavye Kısayolları

| Kısayol | İşlev |
| :--- | :--- |
| `Ctrl + K` | Hızlı Komut & Modül Paletini Aç / Kapat |
| `↑` / `↓` + `Enter` | Komut Paletinde Modül Seç ve Çalıştır |
| `Esc` | Komut Paletini Kapat |

---

## Kurulum, Derleme & Taşınabilir Sürüm (`.exe`)

### Gereksinimler
- **.NET 10 SDK** (`net10.0-windows`)
- **Windows 10 / Windows 11 (x64)**

### Komut Satırı ile Derleme & Test
```powershell
dotnet restore FreeCleaner.slnx
dotnet build FreeCleaner.slnx --configuration Release --no-restore
dotnet test FreeCleaner.Tests/FreeCleaner.Tests.csproj --configuration Release --no-build
dotnet run --project FreeCleaner/FreeCleaner.csproj
```

### Tek Dosya Taşınabilir `.exe` Yayınlama
```powershell
.\scripts\Publish-Portable.ps1
```
Çıktı dosyası `artifacts/portable/FreeCleaner.exe` konumunda oluşturulur. GitHub üzerinde `v*` (örneğin `git tag v4.0.0 && git push origin v4.0.0`) etiketi gönderildiğinde `.github/workflows/release.yml` otomatik olarak testleri çalıştırıp tek dosya `.exe` ve `.zip` paketini GitHub Releases sayfasında yayınlar.
