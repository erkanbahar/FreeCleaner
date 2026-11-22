# Contributing to FreeCleaner Pro

Contributions, bug fixes, localization improvements, and unit tests are welcome!

## Prerequisites

- **Windows 10 / 11 (x64)**
- **.NET 10 SDK** (`net10.0-windows`)

## Local Build & Strict Verification

All pull requests and main branch commits must build with **0 warnings** (`-p:TreatWarningsAsErrors=true`) and pass all xUnit tests:

```powershell
dotnet restore FreeCleaner.slnx
dotnet build FreeCleaner.slnx --configuration Release --no-restore -p:TreatWarningsAsErrors=true
dotnet test FreeCleaner.Tests/FreeCleaner.Tests.csproj --configuration Release --no-build --verbosity normal
```

## Coding & Localization Standards

1. **100% Bilingual UI (`tr-TR` / `en-US`)**:
   - Never hardcode UI labels, headers, tooltips, or status strings directly in `FreeCleaner/Views/*.xaml` or `MainWindow.xaml`.
   - Bind UI strings with `{DynamicResource Loc_...}` and add matching translations to **both** `TrStrings` and `EnStrings` in `FreeCleaner/Services/LocalizationService.cs`.
   - The automated test suite (`Localization_All57Views_AreBilingual_AndHaveCompleteKeyParity`) verifies full dictionary parity and rejects hardcoded Turkish characters in XAML views.
2. **Modular Architecture**:
   - Keep module UIs inside `FreeCleaner/Views/*.xaml(.cs)` registered through `LazyViewRegistry` (no inline module sections in `MainWindow.xaml`).
   - Place cohesive view-model logic in domain-specific `MainViewModel.*.cs` partials and data models in `FreeCleaner/Models/*Models.cs`.
3. **Portable Mode Compliance**:
   - Store persistent settings, telemetry, crash logs, and backups under `AppStoragePathsService.GetDataDirectory()` so USB `portable.flag` deployments stay self-contained.
4. **Pull Requests**:
   - Explain the problem and solution concisely, include unit tests for any new logic or bug fix, and confirm `TreatWarningsAsErrors=true` passes cleanly.
