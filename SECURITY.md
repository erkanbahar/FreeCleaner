# Security Policy — FreeCleaner Pro

## Supported Versions

| Version | Supported |
| :--- | :--- |
| `4.x` (Latest) | ✅ Active security & bug fixes |
| `< 4.0` | ❌ Upgrade to latest `4.x` release |

## Security Architecture Highlights

- **Zero-Data-Loss Safeguards**: Default cleanup prefers Windows Recycle Bin and skips protected system paths (`ExcludedPaths`, `SmartDiskAdvisorService.IsProtectedSystemPath`).
- **SHA-256 Verified Self-Updater**: GitHub release downloads in `AppUpdateService` validate cryptographic SHA-256 digests (`SHA256SUMS.txt` / GitHub asset digest) before executing atomic replacement and clean up tampered files automatically.
- **AES-256-GCM File Vault (`.fcvault`)**: Uses PBKDF2 (`SHA-256`, 100,000 iterations), 128-bit random salt, 96-bit nonce, and authenticated encryption via .NET `AesGcm`.
- **PII & Secret Redaction**: `CrashGuardService` and Diagnostic Support Bundle exports sanitize `%USERPROFILE%` paths, `Bearer` tokens, `password`, `apikey`, and `secret` key/value pairs before writing logs.

## Reporting a Vulnerability

Please do **not** open public GitHub issues with exploit details or sensitive traces.

1. Use GitHub **Private Vulnerability Reporting** (**Security → Advisories → Report a vulnerability**) on [`erkanbahar/FreeCleaner`](https://github.com/erkanbahar/FreeCleaner/security/advisories/new).
2. Include the FreeCleaner version (`v4.x`), Windows version, clear reproduction steps, and potential impact.
3. Expect an initial response within 72 hours and a coordinated fix/release prior to public disclosure.
