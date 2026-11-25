using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FreeCleaner.Models;
using FreeCleaner.Services;
using Xunit;

namespace FreeCleaner.Tests;

public class V47UpgradesTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FreeCleaner.slnx")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate FreeCleaner.slnx from test output directory.");
    }

    [Fact]
    public void AppUpdateService_SelectPortableReleaseAsset_PrioritizesPortableExeOverSetupAndSkipsWingetManifests()
    {
        Assert.True(AppUpdateService.IsTrustedDownloadUri("https://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/FreeCleaner.exe"));
        Assert.True(AppUpdateService.IsTrustedDownloadUri("https://objects.githubusercontent.com/github-production-release-asset/123"));
        Assert.False(AppUpdateService.IsTrustedDownloadUri("http://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/FreeCleaner.exe"));
        Assert.False(AppUpdateService.IsTrustedDownloadUri("https://evil.example.com/FreeCleaner.exe"));
        Assert.False(AppUpdateService.IsTrustedDownloadUri("not-a-url"));

        const string exeAndSetupJson = """
        {
          "assets": [
            { "name": "FreeCleaner.exe", "browser_download_url": "http://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/insecure.exe" },
            { "name": "FreeCleaner.exe", "browser_download_url": "https://evil.example/FreeCleaner.exe", "digest": "sha256:1111111111111111111111111111111111111111111111111111111111111111" },
            { "name": "FreeCleaner-Winget-Manifests-v4.7.0.zip", "browser_download_url": "https://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/FreeCleaner-Winget-Manifests-v4.7.0.zip" },
            { "name": "FreeCleaner.exe", "browser_download_url": "https://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/FreeCleaner.exe", "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
            { "name": "FreeCleaner-Setup-win-x64-v4.7.0.exe", "browser_download_url": "https://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/FreeCleaner-Setup-win-x64-v4.7.0.exe" },
            { "name": "SHA256SUMS.txt", "browser_download_url": "https://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/SHA256SUMS.txt" }
          ]
        }
        """;

        using JsonDocument doc1 = JsonDocument.Parse(exeAndSetupJson);
        var sel1 = AppUpdateService.SelectPortableReleaseAsset(doc1.RootElement);
        Assert.Equal("FreeCleaner.exe", sel1.AssetName);
        Assert.Equal("https://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/FreeCleaner.exe", sel1.AssetUrl);
        Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", sel1.ExpectedSha);
        Assert.Equal("https://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/SHA256SUMS.txt", sel1.ChecksumsUrl);

        // When only Setup.exe + Winget zip + Portable zips exist, Setup and Winget are skipped cleanly
        const string portableZipsJson = """
        {
          "assets": [
            { "name": "FreeCleaner-Setup-win-x64-v4.7.0.exe", "browser_download_url": "https://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/FreeCleaner-Setup-win-x64-v4.7.0.exe" },
            { "name": "FreeCleaner-Winget-Manifests-v4.7.0.zip", "browser_download_url": "https://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/FreeCleaner-Winget-Manifests-v4.7.0.zip" },
            { "name": "FreeCleaner-Portable-USB-win-x64-v4.7.0.zip", "browser_download_url": "https://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/FreeCleaner-Portable-USB-win-x64-v4.7.0.zip" },
            { "name": "FreeCleaner-Portable-win-x64-v4.7.0.zip", "browser_download_url": "https://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/FreeCleaner-Portable-win-x64-v4.7.0.zip" }
          ]
        }
        """;

        using JsonDocument doc2 = JsonDocument.Parse(portableZipsJson);
        var stdZip = AppUpdateService.SelectPortableReleaseAsset(doc2.RootElement, preferUsbPortable: false);
        Assert.Equal("FreeCleaner-Portable-win-x64-v4.7.0.zip", stdZip.AssetName);

        var usbZip = AppUpdateService.SelectPortableReleaseAsset(doc2.RootElement, preferUsbPortable: true);
        Assert.Equal("FreeCleaner-Portable-USB-win-x64-v4.7.0.zip", usbZip.AssetName);
    }

    [Fact]
    public async Task AppUpdateService_DownloadAndVerifyUpdateAsync_RejectsUntrustedHost_AndEnforcesRequiredHash()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"FreeCleanerV47Upd_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            using var client = new HttpClient(new EmptyOkHttpHandler());

            string untrustedDest = Path.Combine(tempDir, "untrusted.exe");
            UpdateDownloadResult untrustedResult = await AppUpdateService.DownloadAndVerifyUpdateAsync(
                "https://untrusted.example.com/FreeCleaner.exe",
                untrustedDest,
                expectedSha256: null,
                httpClient: client);

            Assert.False(untrustedResult.Success);
            Assert.False(File.Exists(untrustedDest));

            string insecureHttpDest = Path.Combine(tempDir, "insecure.exe");
            UpdateDownloadResult httpResult = await AppUpdateService.DownloadAndVerifyUpdateAsync(
                "http://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/FreeCleaner.exe",
                insecureHttpDest,
                expectedSha256: null,
                httpClient: client);

            Assert.False(httpResult.Success);
            Assert.False(File.Exists(insecureHttpDest));

            string missingHashDest = Path.Combine(tempDir, "missing_hash.exe");
            UpdateDownloadResult missingHashResult = await AppUpdateService.DownloadAndVerifyUpdateAsync(
                "https://github.com/erkanbahar/FreeCleaner/releases/download/v4.7.0/FreeCleaner.exe",
                missingHashDest,
                expectedSha256: null,
                httpClient: client,
                requireHash: true);

            Assert.False(missingHashResult.Success);
            Assert.False(missingHashResult.HashVerified);
            Assert.False(File.Exists(missingHashDest));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class EmptyOkHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("stub"u8.ToArray()) });
    }

    [Fact]
    public async Task FileShredderAndWiperService_ProtectsSystemPaths_AndDoesNotFollowDirectoryJunctions()
    {
        Assert.True(FileShredderAndWiperService.IsProtectedSystemPath(@"C:\"));
        Assert.True(FileShredderAndWiperService.IsProtectedSystemPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        Assert.True(FileShredderAndWiperService.IsProtectedSystemPath(Environment.SystemDirectory));
        Assert.True(FileShredderAndWiperService.IsProtectedSystemPath(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
        Assert.True(FileShredderAndWiperService.IsProtectedSystemPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Assert.True(FileShredderAndWiperService.IsProtectedSystemPath(AppContext.BaseDirectory));

        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerShredV47_{Guid.NewGuid():N}");
        string externalDir = Path.Combine(tempRoot, "external_keep");
        string externalFile = Path.Combine(externalDir, "important.txt");
        string shredDir = Path.Combine(tempRoot, "shred_target");
        string disposableFile = Path.Combine(shredDir, "secret.txt");
        string junctionPath = Path.Combine(shredDir, "linked_external");

        Directory.CreateDirectory(externalDir);
        Directory.CreateDirectory(shredDir);
        await File.WriteAllTextAsync(externalFile, "DO NOT TOUCH EXTERNAL DATA");
        await File.WriteAllTextAsync(disposableFile, "top secret payload to shred");

        try
        {
            ProcessStartInfo mklinkPsi = SafeProcessRunner.BuildDirectProcessStartInfo(
                "cmd.exe",
                ["/c", "mklink", "/J", junctionPath, externalDir],
                redirectOutput: true);
            ProcessRunResult mklinkResult = await SafeProcessRunner.RunAsync(mklinkPsi, TimeSpan.FromSeconds(5));
            Assert.True(mklinkResult.Started && mklinkResult.ExitCode == 0, mklinkResult.StandardError);
            Assert.True(FileShredderAndWiperService.IsReparsePoint(junctionPath));

            var blockedSysItem = new ShredTargetItem
            {
                FullPath = Environment.SystemDirectory,
                Name = "System32",
                SizeBytes = 0,
                IsDirectory = true
            };
            var shredDirItem = new ShredTargetItem
            {
                FullPath = shredDir,
                Name = "shred_target",
                SizeBytes = new FileInfo(disposableFile).Length,
                IsDirectory = true
            };

            var (shreddedCount, _) = await FileShredderAndWiperService.ShredItemsAsync([blockedSysItem, shredDirItem], passes: 1);

            Assert.Equal(1, shreddedCount);
            Assert.False(Directory.Exists(shredDir));
            Assert.False(File.Exists(disposableFile));

            // Junction target outside shred_target must survive completely untouched
            Assert.True(File.Exists(externalFile));
            Assert.Equal("DO NOT TOUCH EXTERNAL DATA", await File.ReadAllTextAsync(externalFile));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task SafeProcessRunner_RunAsync_ReadsStdOutAndStdErrConcurrently_AndKillsOnTimeout()
    {
        ProcessStartInfo dualPsi = SafeProcessRunner.BuildDirectProcessStartInfo(
            "cmd.exe",
            ["/c", "echo OUT_TOKEN_47 & echo ERR_TOKEN_47 1>&2"],
            redirectOutput: true);

        ProcessRunResult dualResult = await SafeProcessRunner.RunAsync(dualPsi, TimeSpan.FromSeconds(5));
        Assert.True(dualResult.Started);
        Assert.False(dualResult.TimedOut);
        Assert.Equal(0, dualResult.ExitCode);
        Assert.Contains("OUT_TOKEN_47", dualResult.StandardOutput);
        Assert.Contains("ERR_TOKEN_47", dualResult.StandardError);

        ProcessStartInfo sleepPsi = SafeProcessRunner.BuildDirectProcessStartInfo(
            "ping.exe",
            ["-n", "20", "127.0.0.1"],
            redirectOutput: true);

        var sw = Stopwatch.StartNew();
        ProcessRunResult timedOutResult = await SafeProcessRunner.RunAsync(sleepPsi, TimeSpan.FromMilliseconds(350));
        sw.Stop();

        Assert.True(timedOutResult.Started);
        Assert.True(timedOutResult.TimedOut);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Process kill took too long: {sw.Elapsed}");
    }

    [Fact]
    public void ErrorLogService_RotatesOversizedLogs_RedactsSecrets_AndIntegratesWithSupportBundle()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerErrLog_{Guid.NewGuid():N}");
        string dataDir = Path.Combine(tempRoot, "data");
        string outZip = Path.Combine(tempRoot, "bundle.zip");

        try
        {
            for (int i = 0; i < 6; i++)
            {
                ErrorLogService.Log(
                    "V47Test",
                    $"Attempt #{i} failed token=secretTokenValue{i} Authorization: Bearer superJwtPayload{i} " + new string('X', 80),
                    dataDirOverride: dataDir,
                    maxBytes: 256);
            }

            string currentLog = ErrorLogService.GetLogPath(dataDir);
            string rotatedLog = currentLog + ".1";
            Assert.True(File.Exists(currentLog));
            Assert.True(File.Exists(rotatedLog));

            string currentText = File.ReadAllText(currentLog, Encoding.UTF8);
            string rotatedText = File.ReadAllText(rotatedLog, Encoding.UTF8);
            Assert.DoesNotContain("secretTokenValue", currentText);
            Assert.DoesNotContain("superJwtPayload", currentText);
            Assert.DoesNotContain("secretTokenValue", rotatedText);
            Assert.Contains("***REDACTED***", currentText);

            IReadOnlyList<string> recent = ErrorLogService.ReadRecentEntries(maxLines: 3, dataDirOverride: dataDir);
            Assert.NotEmpty(recent);
            Assert.True(recent.Count <= 3);

            var metrics = new SystemMetricsSnapshot("Windows 11", "X64", ".NET 10", @"C:\", 500_000_000_000, 250_000_000_000, 50.0, 32_000_000_000, 16_000_000_000, 50.0);
            var rules = new List<CleanerRule>
            {
                new() { Id = "tmp", Name = "Temp", Category = "System", Description = "Temp", IconGlyph = "\uE7F8", Group = RuleGroup.System }
            };

            AppStoragePathsService.ExportSupportBundleZip(
                outZip,
                metrics,
                rules,
                totalFreedBytes: 2048,
                totalDeletedFiles: 2,
                logs: ["Operation completed token=hiddenCliToken99"],
                dataDirOverride: dataDir);

            using ZipArchive zip = ZipFile.OpenRead(outZip);
            Assert.NotNull(zip.GetEntry("errors_sanitized.log"));
            Assert.NotNull(zip.GetEntry("errors_previous_sanitized.log"));

            ZipArchiveEntry summaryEntry = Assert.Single(zip.Entries, e => e.FullName == "system_summary.json");
            using var reader = new StreamReader(summaryEntry.Open(), Encoding.UTF8);
            string summaryJson = reader.ReadToEnd();
            Assert.Contains("RuntimeDiagnostics", summaryJson);
            Assert.Contains("RecentErrorLineCount", summaryJson);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void CompiledRegexesAndBackupManager_WorkDeterministically()
    {
        const string sampleWinget = """
            Name                        Id                          Version  Available Source
            ----------------------------------------------------------------------------------
            Git                         Git.Git                     2.46.0   2.47.1    winget
            Visual Studio Code          Microsoft.VisualStudioCode  1.94.0   1.95.0    winget
            """;

        List<WingetUpgradeItem> updates = NinthGenWingetAndNetworkSuiteService.ParseWingetUpgradeOutput(sampleWinget);
        Assert.Equal(2, updates.Count);
        Assert.Equal("Git.Git", updates[0].PackageId);
        Assert.Equal("Microsoft.VisualStudioCode", updates[1].PackageId);

        ClipboardAuditEntryItem tokenAudit = SeventhGenSecurityAndPowerSuiteService.InspectAndSanitizeText("sk-1234567890abcdefghijklmno");
        Assert.Equal(string.Empty, tokenAudit.SanitizedContent);

        ClipboardAuditEntryItem ibanAudit = SeventhGenSecurityAndPowerSuiteService.InspectAndSanitizeText("TR33 0006 1005 1978 6457 8413 26");
        Assert.Equal("TR330006100519786457841326", ibanAudit.SanitizedContent);

        ClipboardAuditEntryItem urlAudit = SeventhGenSecurityAndPowerSuiteService.InspectAndSanitizeText("https://example.com/item?utm_source=test&id=42");
        Assert.Equal("https://example.com/item?id=42", urlAudit.SanitizedContent);

        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerBackupV47_{Guid.NewGuid():N}");
        string regDir = Path.Combine(tempRoot, "RegistryBackups");
        string legacyHostsDir = Path.Combine(tempRoot, "LegacyHosts");
        Directory.CreateDirectory(regDir);
        Directory.CreateDirectory(legacyHostsDir);

        try
        {
            string fNew = Path.Combine(regDir, "new.reg");
            string fMid = Path.Combine(regDir, "mid.reg");
            string fOld = Path.Combine(regDir, "old.reg");
            File.WriteAllText(fNew, "Windows Registry Editor Version 5.00");
            File.WriteAllText(fMid, "Windows Registry Editor Version 5.00");
            File.WriteAllText(fOld, "Windows Registry Editor Version 5.00");

            DateTime now = new(2026, 11, 22, 12, 0, 0);
            File.SetLastWriteTime(fNew, now.AddDays(-1));
            File.SetLastWriteTime(fMid, now.AddDays(-10));
            File.SetLastWriteTime(fOld, now.AddDays(-45));

            List<BackupFileItem> listed = BackupManagerService.ListBackups(tempRoot, legacyHostsDir);
            Assert.Equal(3, listed.Count);

            var (pruned, _, _) = BackupManagerService.PruneOldBackups(
                keepLatestPerType: 2,
                maxAgeDays: 30,
                dataDirectoryOverride: tempRoot,
                legacyHostsDirectoryOverride: legacyHostsDir,
                nowOverride: now);
            Assert.Equal(1, pruned);
            Assert.False(File.Exists(fOld));

            listed = BackupManagerService.ListBackups(tempRoot, legacyHostsDir);
            Assert.Equal(2, listed.Count);
            var (deleted, _, _) = BackupManagerService.DeleteBackups([listed[^1]]);
            Assert.Equal(1, deleted);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Architecture_V47_ZeroPipeDeadlocks_ZeroRawPowerShellCommand_ZeroThreadSleep_AndSupplyChainHardening()
    {
        string repoRoot = FindRepoRoot();
        string servicesDir = Path.Combine(repoRoot, "FreeCleaner", "Services");

        var dualReadPattern = new Regex(@"StandardOutput\.ReadToEnd(?:Async)?\([^)]*\)\s*\+[\s\S]{0,80}StandardError\.ReadToEnd(?:Async)?\(", RegexOptions.Compiled);
        var consecutiveReadPattern = new Regex(@"StandardOutput\.ReadToEnd(?:Async)?\([^)]*\)[\s\S]{0,80}StandardError\.ReadToEnd(?:Async)?\(", RegexOptions.Compiled);
        var rawPsCommandPattern = new Regex(@"Arguments\s*=\s*\$?""-NoProfile\s+-Command", RegexOptions.Compiled);

        foreach (string csFile in Directory.GetFiles(servicesDir, "*.cs", SearchOption.AllDirectories))
        {
            string code = File.ReadAllText(csFile, Encoding.UTF8);
            string fileName = Path.GetFileName(csFile);

            Assert.False(dualReadPattern.IsMatch(code), $"Dual synchronous pipe concatenation found in {fileName}.");
            Assert.False(consecutiveReadPattern.IsMatch(code), $"Sequential stdout/stderr ReadToEnd found in {fileName}.");
            Assert.False(rawPsCommandPattern.IsMatch(code), $"Raw powershell -Command interpolation found in {fileName}.");
            Assert.DoesNotContain("Thread.Sleep(", code, StringComparison.Ordinal);
        }

        Assert.True(File.Exists(Path.Combine(repoRoot, "Directory.Build.props")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "Directory.Packages.props")));
        Assert.True(File.Exists(Path.Combine(repoRoot, ".github", "workflows", "codeql.yml")));

        string buildProps = File.ReadAllText(Path.Combine(repoRoot, "Directory.Build.props"), Encoding.UTF8);
        Assert.Contains("<EnableNETAnalyzers>true</EnableNETAnalyzers>", buildProps);
        Assert.Contains("<NuGetAudit>true</NuGetAudit>", buildProps);

        string releaseYml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"), Encoding.UTF8);
        Assert.Contains("attest-build-provenance", releaseYml);
        Assert.Equal("5.2.0", AppUpdateService.CurrentVersion);
    }
}
