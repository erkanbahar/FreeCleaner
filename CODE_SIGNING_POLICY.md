# Code signing policy

> 🌐 [English](#english) • [Türkçe](#türkçe)

## English

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

> **Status:** FreeCleaner Pro has applied to the SignPath Foundation open-source program. Until the application is approved,
> release files are published unsigned (with SHA-256 checksums and SLSA build provenance attestations).

### What is signed

Only files built from this repository ([`erkanbahar/FreeCleaner`](https://github.com/erkanbahar/FreeCleaner)) by the public
GitHub Actions workflow [`.github/workflows/release.yml`](.github/workflows/release.yml) on GitHub-hosted runners, from a `v*` tag on `main`:

| File | Description |
| --- | --- |
| `FreeCleaner.exe` (win-x64, win-arm64) | Single-file portable application (also inside the `FreeCleaner-Portable-*.zip` archives) |
| `FreeCleaner-Setup-win-x64-v*.exe` | Inno Setup installer, includes an uninstaller |

Files are never signed from a developer machine. Third-party files are not signed with the project certificate.
Signed files carry the product name **FreeCleaner Pro** and the version of the release tag.

### Team roles

FreeCleaner Pro currently has a single maintainer, who holds all roles.

| Role | Members | Responsibility |
| --- | --- | --- |
| Committers and reviewers | [Erkan Bahar (@erkanbahar)](https://github.com/erkanbahar) — [repository collaborators](https://github.com/erkanbahar/FreeCleaner/graphs/contributors) | Commit to `main`; review every contribution (pull request) from people outside this group before it is merged |
| Approvers | [Erkan Bahar (@erkanbahar)](https://github.com/erkanbahar) — repository owner | Manually approve every signing request in SignPath |

All members use multi-factor authentication for GitHub and SignPath.

### Release process

1. Changes reach `main` only through commits by the committers above or reviewed pull requests; the CI workflow builds with warnings as errors and runs the unit tests and the UI smoke test.
2. A version tag `vX.Y.Z` starts `release.yml`, which builds, tests and packages the release on a GitHub-hosted runner.
3. The unsigned files are submitted to SignPath; an approver checks that the request comes from that workflow run and tag, then approves it manually.
4. The signed files, SHA-256 checksums and build provenance attestations are published on [GitHub Releases](https://github.com/erkanbahar/FreeCleaner/releases).

### Privacy

See the [privacy policy](PRIVACY.md). In short: FreeCleaner Pro has no telemetry, no accounts and no advertising.
The only automatic network request is the daily update check against GitHub, which can be switched off.

### Reporting a problem

Report a suspicious or incorrectly signed file privately as described in [SECURITY.md](SECURITY.md).

---

## Türkçe

Ücretsiz kod imzalama [SignPath.io](https://signpath.io) tarafından, sertifika [SignPath Foundation](https://signpath.org) tarafından sağlanmaktadır.

> **Durum:** FreeCleaner Pro, SignPath Foundation açık kaynak programına başvurmuştur. Başvuru onaylanana kadar sürüm dosyaları
> imzasız yayınlanır (SHA-256 özetleri ve SLSA derleme kaynağı doğrulamasıyla).

- **İmzalanan dosyalar:** Yalnızca bu depodan, herkese açık GitHub Actions iş akışı `release.yml` ile GitHub sunucularında, `main` dalındaki bir `v*` etiketinden derlenen `FreeCleaner.exe` (win-x64, win-arm64) ve `FreeCleaner-Setup-win-x64-v*.exe` kurulum dosyası. Geliştirici bilgisayarında imzalama yapılmaz; üçüncü taraf dosyalar proje sertifikasıyla imzalanmaz.
- **Roller:** Proje şu an tek geliştiricilidir; tüm roller [Erkan Bahar (@erkanbahar)](https://github.com/erkanbahar) tarafından yürütülür (commit/inceleme ve her imzalama isteğinin elle onayı). GitHub ve SignPath hesaplarında çok faktörlü doğrulama kullanılır.
- **Gizlilik:** [Gizlilik politikası](PRIVACY.md) — telemetri, hesap veya reklam yoktur; tek otomatik ağ isteği kapatılabilen günlük güncelleme kontrolüdür.
- **Sorun bildirme:** Şüpheli veya hatalı imzalanmış bir dosyayı [SECURITY.md](SECURITY.md) dosyasındaki yolla gizli olarak bildirin.
