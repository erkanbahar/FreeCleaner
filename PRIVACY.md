# Privacy policy / Gizlilik politikası

FreeCleaner Pro runs entirely on your computer. It has no telemetry, no user accounts, no advertising and no analytics.
It does not collect, sell or share personal data.

## Automatic network request (can be switched off)

- **Update check:** At most once a day at startup, FreeCleaner Pro asks GitHub for the latest release
  (`https://api.github.com/repos/erkanbahar/FreeCleaner/releases/latest`). Like any web request, this reveals your IP address
  and the app version (in the User-Agent) to GitHub. No other data is sent. GitHub's privacy statement applies:
  https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement
  To switch it off: Settings → "Silently check for updates daily on startup".

## Network use only when you start it

These features contact other systems only when you open or run them:

- Downloading and installing an update (from GitHub Releases, verified with SHA-256).
- Winget app updates (Microsoft's `winget` tool contacts its package sources).
- Network tools: DNS ping test, local network (ARP table) view, connection tests.

## Data stored on your computer

Settings, cleanup history, `activity.log` and `errors.log` are stored only locally in `%LocalAppData%\FreeCleaner`
(or the `data` folder next to `FreeCleaner.exe` in portable mode). They are never uploaded. A support bundle (ZIP) is only
created when you click it, and you decide whether to share it.

---

## Türkçe

FreeCleaner Pro tamamen bilgisayarınızda çalışır. Telemetri, kullanıcı hesabı, reklam veya analiz yoktur.
Kişisel veri toplamaz, satmaz veya paylaşmaz.

**Otomatik ağ isteği (kapatılabilir):** Açılışta en fazla günde bir kez GitHub'a en son sürüm sorulur
(`api.github.com/repos/erkanbahar/FreeCleaner/releases/latest`). Her web isteğinde olduğu gibi IP adresiniz ve uygulama sürümü
GitHub'a iletilir; başka veri gönderilmez. Kapatmak için: Ayarlar → "Açılışta günlük sessiz güncelleme kontrolü yap".

**Yalnızca siz başlattığınızda ağ kullanımı:** Güncellemeyi indirme/kurma (GitHub Releases, SHA-256 doğrulamalı), Winget ile
program güncelleme, ağ araçları (DNS ping testi, yerel ağ/ARP görünümü, bağlantı testleri).

**Bilgisayarınızda saklanan veriler:** Ayarlar, temizlik geçmişi, `activity.log` ve `errors.log` yalnızca yerelde
(`%LocalAppData%\FreeCleaner` veya taşınabilir modda `FreeCleaner.exe` yanındaki `data` klasörü) tutulur ve hiçbir yere gönderilmez.
Teşhis paketi (ZIP) yalnızca siz tıkladığınızda oluşturulur; paylaşıp paylaşmamak size kalmıştır.
