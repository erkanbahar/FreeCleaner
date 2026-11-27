using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FreeCleaner.Services;
using Xunit;

namespace FreeCleaner.Tests;

public class V48UpgradesTests
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
    public async Task CryptoVaultV2_StreamsMultiChunkEncryptDecrypt_RejectsTamperedHeaderOrTruncation_AndSupportsLegacyV1()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerVaultV48_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            const string password = "VaultV48#Str0ngPassword!";
            string multiChunkSrc = Path.Combine(tempRoot, "large_sample.bin");
            byte[] payload = new byte[10_500];
            RandomNumberGenerator.Fill(payload);
            await File.WriteAllBytesAsync(multiChunkSrc, payload);

            // Encrypt across multiple 2048-byte chunks
            var enc = await EighthGenAiAndSecuritySuiteService.EncryptFileAes256GcmAsync(
                multiChunkSrc,
                password,
                chunkSizeBytes: 2048);

            Assert.True(enc.Success, enc.Message);
            Assert.True(File.Exists(enc.OutputFilePath));
            Assert.Empty(Directory.GetFiles(tempRoot, "*.tmp"));

            byte[] vaultBytes = await File.ReadAllBytesAsync(enc.OutputFilePath);
            Assert.Equal("FCVAULT2"u8.ToArray(), vaultBytes.AsSpan(0, 8).ToArray());

            var decOk = await EighthGenAiAndSecuritySuiteService.DecryptFileAes256GcmAsync(enc.OutputFilePath, password);
            Assert.True(decOk.Success, decOk.Message);
            byte[] restored = await File.ReadAllBytesAsync(decOk.OutputFilePath);
            Assert.Equal(payload, restored);

            // Wrong password must fail cleanly with zero temp leaks
            var wrongPass = await EighthGenAiAndSecuritySuiteService.DecryptFileAes256GcmAsync(enc.OutputFilePath, "Wrong#Pass48");
            Assert.False(wrongPass.Success);
            Assert.Empty(Directory.GetFiles(tempRoot, "*.tmp"));

            // Header tampering (iteration/salt mutation covered by AAD) must fail
            byte[] tamperedHeader = (byte[])vaultBytes.Clone();
            tamperedHeader[20] ^= 0x01;
            string tamperedHeaderPath = Path.Combine(tempRoot, "tampered_header.fcvault");
            await File.WriteAllBytesAsync(tamperedHeaderPath, tamperedHeader);
            var decHeaderTampered = await EighthGenAiAndSecuritySuiteService.DecryptFileAes256GcmAsync(tamperedHeaderPath, password);
            Assert.False(decHeaderTampered.Success);

            // Dropping final chunk must fail AAD isFinalChunk / incomplete stream verification
            string truncatedPath = Path.Combine(tempRoot, "truncated.fcvault");
            await File.WriteAllBytesAsync(truncatedPath, vaultBytes.AsSpan(0, vaultBytes.Length - 2200).ToArray());
            var decTruncated = await EighthGenAiAndSecuritySuiteService.DecryptFileAes256GcmAsync(truncatedPath, password);
            Assert.False(decTruncated.Success);
            Assert.Empty(Directory.GetFiles(tempRoot, "*.tmp"));

            // Empty 0-byte file round-trip
            string emptySrc = Path.Combine(tempRoot, "empty.bin");
            await File.WriteAllBytesAsync(emptySrc, []);
            var encEmpty = await EighthGenAiAndSecuritySuiteService.EncryptFileAes256GcmAsync(emptySrc, password);
            Assert.True(encEmpty.Success, encEmpty.Message);
            var decEmpty = await EighthGenAiAndSecuritySuiteService.DecryptFileAes256GcmAsync(encEmpty.OutputFilePath, password);
            Assert.True(decEmpty.Success, decEmpty.Message);
            Assert.Empty(await File.ReadAllBytesAsync(decEmpty.OutputFilePath));

            // Backward-compatible legacy FCVAULT1 (100,000 PBKDF2 iterations) decryption check
            byte[] legacyPlain = "Legacy V1 Vault Payload v4.7"u8.ToArray();
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] tag = new byte[16];
            byte[] cipher = new byte[legacyPlain.Length];
            byte[] legacyKey = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, 100_000, HashAlgorithmName.SHA256, 32);
            using (var aes = new AesGcm(legacyKey, 16))
                aes.Encrypt(nonce, legacyPlain, cipher, tag);

            string legacyPath = Path.Combine(tempRoot, "legacy.dat.fcvault");
            using (var fs = File.Create(legacyPath))
            {
                fs.Write("FCVAULT1"u8);
                fs.Write(salt);
                fs.Write(nonce);
                fs.Write(tag);
                fs.Write(cipher);
            }

            var decLegacy = await EighthGenAiAndSecuritySuiteService.DecryptFileAes256GcmAsync(legacyPath, password);
            Assert.True(decLegacy.Success, decLegacy.Message);
            Assert.Equal(legacyPlain, await File.ReadAllBytesAsync(decLegacy.OutputFilePath));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AtomicFileStorageService_WritesAtomically_CreatesBackup_AndRecoversFromCorruptJson()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerAtomicV48_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string settingsPath = Path.Combine(tempRoot, "settings.json");
            AtomicFileStorageService.WriteAllTextAtomic(
                settingsPath,
                "{\"SelectedTheme\":\"ObsidianNeon\",\"SelectedLanguage\":\"tr-TR\"}",
                createBackup: true);

            Assert.True(File.Exists(settingsPath));
            Assert.False(File.Exists(settingsPath + ".bak"));

            AtomicFileStorageService.WriteAllTextAtomic(
                settingsPath,
                "{\"SelectedTheme\":\"FluentLight\",\"SelectedLanguage\":\"en-US\"}",
                createBackup: true);

            Assert.True(File.Exists(settingsPath + ".bak"));
            Assert.Contains("ObsidianNeon", File.ReadAllText(settingsPath + ".bak"));
            Assert.Empty(Directory.GetFiles(tempRoot, "*.tmp"));

            // Corrupt primary file and verify automatic recovery from .bak + healing
            File.WriteAllText(settingsPath, "{ broken json without closing brace");
            CleanerConfigData? recovered = AtomicFileStorageService.ReadJsonWithBackupFallback<CleanerConfigData>(
                settingsPath,
                logArea: "V48AtomicTest");

            Assert.NotNull(recovered);
            Assert.Equal("ObsidianNeon", recovered!.SelectedTheme);
            Assert.Contains("ObsidianNeon", File.ReadAllText(settingsPath));

            // Verify CleanupHistoryService atomic write/read round-trip
            string historyPath = Path.Combine(tempRoot, "history.json");
            CleanupHistoryService.RecordSession("V48Test", 1024 * 1024, 5, historyPath);
            Assert.Single(CleanupHistoryService.LoadHistory(historyPath));
            Assert.Empty(Directory.GetFiles(tempRoot, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PInvokeServices_AndOrphanSafety_WorkDeterministically()
    {
        SystemMetricsSnapshot metrics = SystemInfoService.GetMetrics();
        Assert.True(metrics.RamTotalBytes > 0);
        Assert.True(metrics.RamAvailableBytes >= 0);

        List<string> browsers = SystemInfoService.GetRunningBrowsers();
        Assert.NotNull(browsers);

        (long rbSize, int rbCount) = RecycleBinHelper.QueryRecycleBin();
        Assert.True(rbSize >= 0);
        Assert.True(rbCount >= 0);

        List<FreeCleaner.Models.OrphanAppFolderItem> emptyAppsOrphans =
            await UninstallerAndOrphanService.ScanOrphanFoldersAsync([]);
        Assert.NotNull(emptyAppsOrphans);
    }

    [Fact]
    public void Architecture_V48_FluentLightThemeHygiene_ZeroHardcodedDarkSurfaceHexColors()
    {
        string repoRoot = FindRepoRoot();
        string viewsDir = Path.Combine(repoRoot, "FreeCleaner", "Views");
        string mainWindowPath = Path.Combine(repoRoot, "FreeCleaner", "MainWindow.xaml");

        string[] forbiddenDarkHexes =
        [
            "#0D1422",
            "#1D1622",
            "#4C1D24",
            "#222D40",
            "#263859",
            "#1E2B45",
            "#F87171"
        ];

        List<string> xamlFiles = Directory.GetFiles(viewsDir, "*.xaml", SearchOption.AllDirectories).ToList();
        xamlFiles.Add(mainWindowPath);

        foreach (string xamlPath in xamlFiles)
        {
            string content = File.ReadAllText(xamlPath, Encoding.UTF8);
            string fileName = Path.GetFileName(xamlPath);
            foreach (string hex in forbiddenDarkHexes)
            {
                Assert.False(
                    content.Contains(hex, StringComparison.OrdinalIgnoreCase),
                    $"Hardcoded dark theme color '{hex}' found in {fileName}; bind to DynamicResource brush instead.");
            }
        }
    }

    [Fact]
    public void Architecture_V48_AllWin32PInvokesEnforceSystem32SearchPath_AndUseLibraryImport()
    {
        string repoRoot = FindRepoRoot();
        string servicesDir = Path.Combine(repoRoot, "FreeCleaner", "Services");

        var pinvokeRegex = new Regex(@"\[(DllImport|LibraryImport)\(", RegexOptions.Compiled);
        int libraryImportCount = 0;

        foreach (string csFile in Directory.GetFiles(servicesDir, "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(csFile, Encoding.UTF8);
            string fileName = Path.GetFileName(csFile);

            for (int i = 0; i < lines.Length; i++)
            {
                Match m = pinvokeRegex.Match(lines[i]);
                if (!m.Success)
                    continue;

                if (m.Groups[1].Value == "LibraryImport")
                    libraryImportCount++;

                string window = string.Join('\n', lines.Skip(i).Take(4));
                Assert.True(
                    window.Contains("DefaultDllImportSearchPaths(DllImportSearchPath.System32)", StringComparison.Ordinal),
                    $"P/Invoke at {fileName}:L{i + 1} is missing [DefaultDllImportSearchPaths(DllImportSearchPath.System32)].");
            }
        }

        Assert.True(libraryImportCount >= 9, $"Expected >= 9 [LibraryImport] P/Invokes, found {libraryImportCount}.");
    }

    [Fact]
    public void Architecture_V48_Arm64ReleaseAndCiCoverageSummary_ConfiguredForVersion500()
    {
        string repoRoot = FindRepoRoot();
        Assert.Equal("5.2.1", AppUpdateService.CurrentVersion);

        string publishScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "Publish-Portable.ps1"), Encoding.UTF8);
        Assert.Contains("win-arm64", publishScript);

        string releaseYml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"), Encoding.UTF8);
        Assert.Contains("FreeCleaner-Portable-win-arm64-", releaseYml);
        Assert.Contains("attest-build-provenance", releaseYml);

        string ciYml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "dotnet-ci.yml"), Encoding.UTF8);
        Assert.Contains("GITHUB_STEP_SUMMARY", ciYml);
    }
}
