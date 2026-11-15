# FreeCleaner Pro (.NET 10 WPF)

**FreeCleaner Pro**, **.NET 10** ve **WPF (Modern Dark Fluent UI)** ile geliştirilmiş, tam kapsamlı sistem temizleme, bakım, güvenlik ve performans optimizasyon paketidir.

## Öne Çıkan Modüller (21 Modül)

1. **Sistem, Tarayıcı & Uygulama Temizleyici:** Windows Temp, Prefetch, Windows Update önbelleği, Chrome/Edge/Brave/Firefox önbellek ve çerezleri, Geri Dönüşüm Kutusu analizi ve güvenli temizliği (Simülasyon + Geri Dönüşüm Kutusuna Taşıma seçeneği).
2. **Başlangıç (Startup) Yöneticisi:** Kayıt Defteri (`HKCU`/`HKLM Run`) ve Başlangıç klasörü uygulamalarını yönetme.
3. **Kayıt Defteri (Registry) Temizleyici:** Kırık uygulama yolları, geçersiz `SharedDLLs` ve `MuiCache` kayıtlarını tarama ve otomatik `.reg` yedeği alarak onarma.
4. **Büyük Dosya & Kopya (Duplicate) Bulucu:** SHA-256 + kısmi özetleme ile birebir aynı kopyaları ve büyük dosyaları bulma.
5. **Uygulama Kaldırıcı & Yetim (Orphan) Klasör Temizleyici:** Kurulu programları kaldırma ve silinmiş programların `AppData` kalıntılarını temizleme.
6. **Canlı RAM & Ağ Hızlandırıcı (System Booster):** `EmptyWorkingSet` Win32 API ile RAM önbelleğini boşaltma ve DNS/Winsock sıfırlama.
7. **Geliştirici & Oyuncu Derin Temizleyici (DevCleaner):**
   - **Visual Studio Projeleri `Debug`, `Retail`, `Release`, `obj` ve `.vs` Otomatik Temizleyici**
   - `node_modules`, `bin`, `.venv`, `__pycache__` ve DirectX / NVIDIA / AMD / Steam Shader önbellek temizliği.
8. **Askeri Standartta Dosya Öğütücü (File Shredder) & Boş Alan Silici:** DoD 5220.22-M (1, 3, 7 geçişli) geri döndürülemez dosya imhası.
9. **Windows Gizlilik & Telemetri Kalkanı:** Recent Docs, JumpLists, Clipboard, Activity History temizliği ve Telemetri/Bing/Copilot gizlilik anahtarları.
10. **Canlı Ağ (TCP/UDP) İzleyici & Hosts Editörü:** Aktif port/PID bağlantıları ve `hosts` reklam/telemetri engelleme yönetimi.
11. **Zamanlanmış Görevler (`schtasks`) & Klasör Boyut Ağacı (Folder Tree Visualizer):** Windows görevlerini yönetme ve klasör dağılımını yüzdesel görme.
12. **İndirilenler Asistanı & Boş Klasör Temizleyici:** İndirilenler klasöründeki eski kurulum (`.exe`, `.msi`, `.iso`, `.zip`) paketlerini ve boş klasörleri temizleme.
13. **Sağ Tık Menüsü (Shell Context Menu) Yöneticisi:** Windows Gezgini sağ tık menüsü eklentilerini (`HKCR\*\shellex`) denetleme ve kaldırma.
14. **Windows Servisleri & Telemetri Optimizasyonu:** Arka plan servislerini (`DiagTrack`, `SysMain`, `WSearch`, `Spooler`, `Xbox`) tek tıkla yönetme.
15. **S.M.A.R.T. Disk Sağlığı, Güç Planı & Tek Tıkla Oyun Modu:** Fiziksel NVMe/SSD/HDD sağlık durumu, Nihai Performans güç planı, Tek Tıkla Oyun Hızlandırıcı ve `powercfg /batteryreport` HTML pil sağlık raporu.
16. **Sistem Onarım Merkezi (DISM / SFC / FontCache):** Yönetici yetkili `DISM /CheckHealth`, `SFC /scannow` ve bozuk `FontCache` sıfırlama.
17. **Kilitli Dosya & Klasör Çözücü (File Unlocker):** Win32 Restart Manager (`rstrtmgr.dll`) ile dosyayı kilitleyen işlemleri bulma ve sonlandırma.
18. **Tarayıcı Eklentileri (Extensions) Denetçisi:** Chrome, Edge, Brave ve Vivaldi uzantılarını denetleme ve kaldırma.
19. **Windows Güncellemeleri (HotFix KB) & Sürücü Deposu (DriverStore):** Yüklü `KB` yamalarını ve `pnputil` OEM sürücü paketlerini listeleme.
20. **Ortam Değişkeni (`PATH`) Doktoru & Kırık Kısayol (`.lnk`) Temizleyici:** Silinmiş/tekrarlanan `PATH` dizinlerini ve kırık kısayolları onarma.
21. **Beyaz Liste (Exclusions) & HTML/JSON Raporlama:** Korunan klasörler ve detaylı sistem temizlik raporu çıktısı.

## Gereksinimler ve Derleme

- **.NET 10 SDK** (`net10.0-windows`)
- Windows 10 / Windows 11

```powershell
dotnet build FreeCleaner/FreeCleaner.csproj -c Release
dotnet run --project FreeCleaner/FreeCleaner.csproj
```
