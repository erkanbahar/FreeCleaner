using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FreeCleaner.Models;
using FreeCleaner.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FreeCleaner.Tests;

public class V44UpgradesTests
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
    public void WindowPlacementService_ClampToVirtualScreen_HandlesOffScreenAndMinBounds()
    {
        // Disconnected secondary monitor right/bottom off-screen -> clamped inside virtual bounds
        WindowBoundsState offScreenRight = WindowPlacementService.ClampToVirtualScreen(
            new WindowBoundsState(4000, 2500, 1280, 800, IsMaximized: false),
            virtualLeft: 0,
            virtualTop: 0,
            virtualWidth: 1920,
            virtualHeight: 1080);

        Assert.Equal(640, offScreenRight.Left);
        Assert.Equal(280, offScreenRight.Top);
        Assert.Equal(1280, offScreenRight.Width);
        Assert.Equal(800, offScreenRight.Height);
        Assert.False(offScreenRight.IsMaximized);

        // Negative secondary monitor on left edge stays valid when virtualLeft covers it
        WindowBoundsState leftMonitor = WindowPlacementService.ClampToVirtualScreen(
            new WindowBoundsState(-1800, 100, 1200, 750, IsMaximized: true),
            virtualLeft: -1920,
            virtualTop: 0,
            virtualWidth: 3840,
            virtualHeight: 1080);

        Assert.Equal(-1800, leftMonitor.Left);
        Assert.Equal(100, leftMonitor.Top);
        Assert.True(leftMonitor.IsMaximized);

        // Undersized + NaN -> respects minimum bounds; oversized -> clamped to virtual screen
        WindowBoundsState tooSmall = WindowPlacementService.ClampToVirtualScreen(
            new WindowBoundsState(double.NaN, -200, 600, 400, IsMaximized: false),
            virtualLeft: 0,
            virtualTop: 0,
            virtualWidth: 1920,
            virtualHeight: 1080);

        Assert.Equal(0, tooSmall.Left);
        Assert.Equal(0, tooSmall.Top);
        Assert.Equal(1120, tooSmall.Width);
        Assert.Equal(700, tooSmall.Height);

        WindowBoundsState tooLarge = WindowPlacementService.ClampToVirtualScreen(
            new WindowBoundsState(100, 100, 3000, 2000, IsMaximized: false),
            virtualLeft: 0,
            virtualTop: 0,
            virtualWidth: 1920,
            virtualHeight: 1080);

        Assert.Equal(1920, tooLarge.Width);
        Assert.Equal(1080, tooLarge.Height);
        Assert.Equal(0, tooLarge.Left);
        Assert.Equal(0, tooLarge.Top);
    }

    [Fact]
    public void SystemTrayService_FormatTrayTooltip_SwitchesLanguageAndRespects63CharLimit()
    {
        string en = SystemTrayService.FormatTrayTooltip(45, 62, 120, "en-US");
        string tr = SystemTrayService.FormatTrayTooltip(45, 62, 120, "tr-TR");
        string extreme = SystemTrayService.FormatTrayTooltip(999, -5, 999_999_999_999_999_999d, "en-US");

        Assert.Contains("Free 120GB", en);
        Assert.Contains("CPU 45%", en);
        Assert.Contains("RAM 62%", en);
        Assert.True(en.Length <= 63);

        Assert.Contains("Boş 120GB", tr);
        Assert.Contains("CPU %45", tr);
        Assert.Contains("RAM %62", tr);
        Assert.True(tr.Length <= 63);

        Assert.Contains("CPU 100%", extreme);
        Assert.Contains("RAM 0%", extreme);
        Assert.True(extreme.Length <= 63);
    }

    [Fact]
    public void CustomCleaningProfile_AppliesToRules_ParsesFromCli_AndRoundTripsJson()
    {
        var rules = new List<CleanerRule>
        {
            new()
            {
                Id = "temp",
                Name = "Temp Files",
                Category = "System",
                Description = "Temporary files",
                IconGlyph = "\uE7F8",
                Group = RuleGroup.System,
                IsSelected = false
            },
            new()
            {
                Id = "cache",
                Name = "Browser Cache",
                Category = "Browsers",
                Description = "Browser cache",
                IconGlyph = "\uE774",
                Group = RuleGroup.Browsers,
                IsSelected = true
            },
            new()
            {
                Id = "logs",
                Name = "System Logs",
                Category = "System",
                Description = "Old logs",
                IconGlyph = "\uE7F8",
                Group = RuleGroup.System,
                IsSelected = false
            }
        };

        var customProfile = new CustomCleaningProfile
        {
            Name = "WorkClean",
            RuleIds = ["temp", "logs"]
        };

        (int selectedCount, string description) =
            ShellIntegrationAndProfilesService.ApplyCustomProfileToRules(rules, customProfile);

        Assert.Equal(2, selectedCount);
        Assert.Contains("WorkClean", description);
        Assert.True(rules[0].IsSelected);
        Assert.False(rules[1].IsSelected);
        Assert.True(rules[2].IsSelected);

        CliLaunchAction customCli = ShellIntegrationAndProfilesService.ParseStartupArguments(
            ["--dry-run", "--profile", "MyCustomWork", "--exit"]);
        Assert.True(customCli.DryRun);
        Assert.True(customCli.ExitWhenFinished);
        Assert.Null(customCli.Profile);
        Assert.Equal("MyCustomWork", customCli.CustomProfileName);

        CliLaunchAction builtInCli = ShellIntegrationAndProfilesService.ParseStartupArguments(
            ["--profile", "SafeDaily"]);
        Assert.Equal(CleaningProfileKind.SafeDaily, builtInCli.Profile);
        Assert.Null(builtInCli.CustomProfileName);

        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerCfgTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            string settingsPath = Path.Combine(tempRoot, "exported_settings.json");
            var config = new CleanerConfigData
            {
                SelectedLanguage = "en-US",
                SelectedTheme = "FluentLight",
                HasCompletedOnboarding = true,
                WindowLeft = 120,
                WindowTop = 80,
                WindowWidth = 1440,
                WindowHeight = 900,
                WindowMaximized = true,
                CustomProfiles =
                [
                    customProfile,
                    new CustomCleaningProfile { Name = "EveningQuick", RuleIds = ["cache"] }
                ]
            };

            ShellIntegrationAndProfilesService.ExportSettingsToFile(config, settingsPath);
            CleanerConfigData imported = ShellIntegrationAndProfilesService.ImportSettingsFromFile(settingsPath);

            Assert.Equal("en-US", imported.SelectedLanguage);
            Assert.Equal("FluentLight", imported.SelectedTheme);
            Assert.True(imported.HasCompletedOnboarding);
            Assert.Equal(120, imported.WindowLeft);
            Assert.Equal(80, imported.WindowTop);
            Assert.Equal(1440, imported.WindowWidth);
            Assert.Equal(900, imported.WindowHeight);
            Assert.True(imported.WindowMaximized);
            Assert.Equal(2, imported.CustomProfiles.Count);
            Assert.Equal("WorkClean", imported.CustomProfiles[0].Name);
            Assert.Equal(["temp", "logs"], imported.CustomProfiles[0].RuleIds);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Localization_All57Views_AreBilingual_AndHaveCompleteKeyParity()
    {
        string repoRoot = FindRepoRoot();
        string viewsDir = Path.Combine(repoRoot, "FreeCleaner", "Views");
        string mainWindowPath = Path.Combine(repoRoot, "FreeCleaner", "MainWindow.xaml");

        string[] viewFiles = Directory.GetFiles(viewsDir, "*.xaml", SearchOption.TopDirectoryOnly);
        Assert.Equal(57, viewFiles.Length);

        var trKeys = LocalizationService.GetAllKeys("tr-TR").ToHashSet(StringComparer.Ordinal);
        var enKeys = LocalizationService.GetAllKeys("en-US").ToHashSet(StringComparer.Ordinal);

        Assert.True(trKeys.SetEquals(enKeys),
            $"Missing in EN: [{string.Join(", ", trKeys.Except(enKeys))}] | Missing in TR: [{string.Join(", ", enKeys.Except(trKeys))}]");

        foreach (string key in trKeys)
        {
            Assert.False(string.IsNullOrWhiteSpace(LocalizationService.Get(key, "tr-TR")), $"Empty TR key: {key}");
            Assert.False(string.IsNullOrWhiteSpace(LocalizationService.Get(key, "en-US")), $"Empty EN key: {key}");
        }

        var locRefRegex = new Regex(@"\{DynamicResource\s+(Loc_[A-Za-z0-9_]+)\}", RegexOptions.Compiled);
        var turkishCharRegex = new Regex(@"[çğıöşüÇĞİÖŞÜ]", RegexOptions.Compiled);
        string[] checkedAttributes = ["Text", "Content", "Header", "ToolTip"];

        var allXamlFiles = viewFiles.Append(mainWindowPath);
        foreach (string xamlFile in allXamlFiles)
        {
            string raw = File.ReadAllText(xamlFile, Encoding.UTF8);
            foreach (Match match in locRefRegex.Matches(raw))
            {
                string key = match.Groups[1].Value;
                Assert.True(trKeys.Contains(key), $"Missing TR key '{key}' referenced in {Path.GetFileName(xamlFile)}");
                Assert.True(enKeys.Contains(key), $"Missing EN key '{key}' referenced in {Path.GetFileName(xamlFile)}");
            }
        }

        foreach (string viewFile in viewFiles)
        {
            XDocument doc = XDocument.Load(viewFile);
            string viewName = Path.GetFileName(viewFile);

            foreach (XElement elem in doc.Descendants())
            {
                foreach (string attrName in checkedAttributes)
                {
                    XAttribute? attr = elem.Attribute(attrName);
                    if (attr is null || string.IsNullOrWhiteSpace(attr.Value))
                        continue;

                    if (attr.Value.StartsWith('{'))
                        continue;

                    Assert.False(
                        turkishCharRegex.IsMatch(attr.Value),
                        $"Hardcoded Turkish text found in {viewName} <{elem.Name.LocalName} {attrName}=\"{attr.Value}\">");
                }
            }
        }
    }

    [Fact]
    public async Task EighthGen_Aes256GcmVault_RoundTrips_RejectsWrongPasswordAndTampering()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"VaultUnit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string srcPath = Path.Combine(tempRoot, "secret.bin");
            byte[] originalPayload = "Confidential FreeCleaner v4.4 Vault Payload \x01\x02\xFE\xFF"u8.ToArray();
            await File.WriteAllBytesAsync(srcPath, originalPayload);

            var shortPass = await EighthGenAiAndSecuritySuiteService.EncryptFileAes256GcmAsync(srcPath, "abc");
            Assert.False(shortPass.Success);

            var enc = await EighthGenAiAndSecuritySuiteService.EncryptFileAes256GcmAsync(srcPath, "Str0ng!Pass#44");
            Assert.True(enc.Success, enc.Message);
            Assert.True(File.Exists(enc.OutputFilePath));

            var wrongPass = await EighthGenAiAndSecuritySuiteService.DecryptFileAes256GcmAsync(enc.OutputFilePath, "WrongPass!");
            Assert.False(wrongPass.Success);

            var decOk = await EighthGenAiAndSecuritySuiteService.DecryptFileAes256GcmAsync(enc.OutputFilePath, "Str0ng!Pass#44");
            Assert.True(decOk.Success, decOk.Message);
            byte[] restoredPayload = await File.ReadAllBytesAsync(decOk.OutputFilePath);
            Assert.Equal(originalPayload, restoredPayload);

            // Flip a byte inside ciphertext/tag to test GCM authentication failure
            byte[] tampered = await File.ReadAllBytesAsync(enc.OutputFilePath);
            tampered[^1] ^= 0xFF;
            string tamperedPath = Path.Combine(tempRoot, "tampered.fcvault");
            await File.WriteAllBytesAsync(tamperedPath, tampered);

            var decTampered = await EighthGenAiAndSecuritySuiteService.DecryptFileAes256GcmAsync(tamperedPath, "Str0ng!Pass#44");
            Assert.False(decTampered.Success);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task EighthGen_ComputeMultiHashes_AndAiWeightDetection_WorkAccurately()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"HashUnit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string filePath = Path.Combine(tempRoot, "sample.dat");
            byte[] content = "FreeCleaner Pro Multi-Hash Integrity Check v4.4"u8.ToArray();
            await File.WriteAllBytesAsync(filePath, content);

            string expectedSha256 = Convert.ToHexString(SHA256.HashData(content));

            List<FileHashAndVaultResultItem> matched =
                await EighthGenAiAndSecuritySuiteService.ComputeMultiHashesAsync(filePath, expectedSha256);

            Assert.Equal(4, matched.Count);
            FileHashAndVaultResultItem sha256Item = Assert.Single(matched, x => x.AlgorithmOrOperation.StartsWith("SHA-256", StringComparison.Ordinal));
            Assert.Equal(expectedSha256, sha256Item.HashOrOutputValue);
            Assert.Contains("✅", sha256Item.VerificationStatus);

            List<FileHashAndVaultResultItem> mismatched =
                await EighthGenAiAndSecuritySuiteService.ComputeMultiHashesAsync(filePath, new string('0', 64));
            FileHashAndVaultResultItem mismatchItem = Assert.Single(mismatched, x => x.AlgorithmOrOperation.StartsWith("SHA-256", StringComparison.Ordinal));
            Assert.Contains("🚨", mismatchItem.VerificationStatus);

            Assert.True(EighthGenAiAndSecuritySuiteService.IsStandaloneAiWeightFile(@"C:\models\llama-3.gguf", 15L * 1024 * 1024));
            Assert.True(EighthGenAiAndSecuritySuiteService.IsStandaloneAiWeightFile(@"C:\models\model.safetensors", 10L * 1024 * 1024));
            Assert.True(EighthGenAiAndSecuritySuiteService.IsStandaloneAiWeightFile(@"C:\chrome\weights.bin", 60L * 1024 * 1024));
            Assert.False(EighthGenAiAndSecuritySuiteService.IsStandaloneAiWeightFile(@"C:\chrome\weights.bin", 10L * 1024 * 1024));
            Assert.False(EighthGenAiAndSecuritySuiteService.IsStandaloneAiWeightFile(@"C:\models\notes.txt", 100L * 1024 * 1024));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SeventhGen_BatchRename_PreviewApplyAndUndo_RestoresOriginalNames()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"RenameUnit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string fileA = Path.Combine(tempRoot, "photo_alpha.txt");
            string fileB = Path.Combine(tempRoot, "photo_beta.txt");
            File.WriteAllText(fileA, "A");
            File.WriteAllText(fileB, "B");

            List<BatchRenamePreviewItem> preview = SeventhGenSecurityAndPowerSuiteService.BuildBatchRenamePreview(
                tempRoot,
                findText: "photo",
                replaceText: "img",
                prefixText: "v1_",
                suffixText: string.Empty,
                addSequenceNumber: true,
                caseMode: string.Empty);

            Assert.Equal(2, preview.Count);
            Assert.Equal("v1_img_alpha_001.txt", preview[0].NewName);
            Assert.Equal("v1_img_beta_002.txt", preview[1].NewName);

            var (renamedCount, _) = SeventhGenSecurityAndPowerSuiteService.ApplyBatchRename(preview);
            Assert.Equal(2, renamedCount);
            Assert.True(File.Exists(Path.Combine(tempRoot, "v1_img_alpha_001.txt")));
            Assert.True(File.Exists(Path.Combine(tempRoot, "v1_img_beta_002.txt")));
            Assert.False(File.Exists(fileA));

            var (restoredCount, _) = SeventhGenSecurityAndPowerSuiteService.UndoLastBatchRename();
            Assert.Equal(2, restoredCount);
            Assert.True(File.Exists(fileA));
            Assert.True(File.Exists(fileB));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SeventhGen_ClipboardShield_SanitizesTrackingUrls_MasksSecrets_AndNormalizesIban()
    {
        ClipboardAuditEntryItem urlAudit = SeventhGenSecurityAndPowerSuiteService.InspectAndSanitizeText(
            "https://example.com/item?id=42&utm_source=x&fbclid=abc&gclid=123&si=99");

        Assert.Contains("4", urlAudit.CategoryBadge);
        Assert.Contains("id=42", urlAudit.SanitizedContent);
        Assert.DoesNotContain("utm_source", urlAudit.SanitizedContent);
        Assert.DoesNotContain("fbclid", urlAudit.SanitizedContent);
        Assert.DoesNotContain("gclid", urlAudit.SanitizedContent);
        Assert.DoesNotContain("si=99", urlAudit.SanitizedContent);

        ClipboardAuditEntryItem secretAudit = SeventhGenSecurityAndPowerSuiteService.InspectAndSanitizeText(
            "sk-test1234567890abcdefghijklmnop");

        Assert.Contains("API", secretAudit.CategoryBadge);
        Assert.Contains("••••", secretAudit.OriginalPreview);
        Assert.Empty(secretAudit.SanitizedContent);

        ClipboardAuditEntryItem ibanAudit = SeventhGenSecurityAndPowerSuiteService.InspectAndSanitizeText(
            "tr33 0006 1005 1978 6457 8413 26");

        Assert.Contains("IBAN", ibanAudit.CategoryBadge);
        Assert.Equal("TR330006100519786457841326", ibanAudit.SanitizedContent);
    }

    [Fact]
    public void NinthGen_ParseWingetUpgradeOutput_ParsesEnglishAndTurkishTables()
    {
        const string enOutput = """
            Name                          Id                            Version   Available Source
            ------------------------------------------------------------------------------------
            Microsoft Visual Studio Code  Microsoft.VisualStudioCode    1.94.0    1.95.2    winget
            Git                           Git.Git                       2.46.0    2.47.0    winget
            2 upgrades available.
            """;

        List<WingetUpgradeItem> enItems = NinthGenWingetAndNetworkSuiteService.ParseWingetUpgradeOutput(enOutput);
        Assert.Equal(2, enItems.Count);
        Assert.Equal("Microsoft.VisualStudioCode", enItems[0].PackageId);
        Assert.Equal("1.94.0", enItems[0].InstalledVersion);
        Assert.Equal("1.95.2", enItems[0].AvailableVersion);
        Assert.Equal("Git.Git", enItems[1].PackageId);
        Assert.True(enItems[1].IsSelected);

        const string trOutput = """
            Ad                            Kimlik                    Sürüm     Kullanılabilir Kaynak
            ----------------------------------------------------------------------------------------
            Mozilla Firefox               Mozilla.Firefox           131.0     132.0          winget
            Notepad++                     Notepad++.Notepad++       8.6.9     8.7.0          winget
            2 yükseltme mevcut.
            1 paket açık bir hedefleme gerektiren sürüm numaralarına sahip.
            """;

        List<WingetUpgradeItem> trItems = NinthGenWingetAndNetworkSuiteService.ParseWingetUpgradeOutput(trOutput);
        Assert.Equal(2, trItems.Count);
        Assert.Equal("Mozilla.Firefox", trItems[0].PackageId);
        Assert.Equal("Notepad++.Notepad++", trItems[1].PackageId);
        Assert.Empty(NinthGenWingetAndNetworkSuiteService.ParseWingetUpgradeOutput(string.Empty));
    }

    [Fact]
    public async Task NinthGen_SqliteOptimizer_VacuumsAndShrinksTempDatabase()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"SqliteOptUnit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        string dbName = $"test_{Guid.NewGuid():N}.sqlite";
        string dbPath = Path.Combine(tempRoot, dbName);

        try
        {
            string connStr = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
            using (var conn = new SqliteConnection(connStr))
            {
                conn.Open();
                using var createCmd = conn.CreateCommand();
                createCmd.CommandText = "CREATE TABLE items (id INTEGER PRIMARY KEY, payload TEXT);";
                createCmd.ExecuteNonQuery();

                string bigPayload = new string('X', 2048);
                using var tx = conn.BeginTransaction();
                using var insertCmd = conn.CreateCommand();
                insertCmd.Transaction = tx;
                insertCmd.CommandText = "INSERT INTO items (payload) VALUES ($p);";
                SqliteParameter param = insertCmd.Parameters.Add("$p", SqliteType.Text);
                for (int i = 0; i < 400; i++)
                {
                    param.Value = bigPayload;
                    insertCmd.ExecuteNonQuery();
                }
                tx.Commit();

                using var deleteCmd = conn.CreateCommand();
                deleteCmd.CommandText = "DELETE FROM items;";
                deleteCmd.ExecuteNonQuery();
            }

            SqliteConnection.ClearAllPools();
            long beforeSize = new FileInfo(dbPath).Length;

            var item = new SqliteDatabaseItem
            {
                AppName = "UnitTest",
                DatabaseName = dbName,
                FullFilePath = dbPath,
                TotalSizeBytes = beforeSize,
                ReclaimableBytes = beforeSize / 2,
                HealthDetails = "Test bloated DB",
                IsSelected = true
            };

            var (optimizedCount, _, message) =
                await NinthGenWingetAndNetworkSuiteService.OptimizeSelectedSqliteDatabasesAsync([item]);

            SqliteConnection.ClearAllPools();
            long afterSize = new FileInfo(dbPath).Length;

            Assert.Equal(1, optimizedCount);
            Assert.True(afterSize < beforeSize, $"Expected VACUUM to shrink DB ({beforeSize} -> {afterSize}). Msg: {message}");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(tempRoot, recursive: true); } catch { }

            string backupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FreeCleaner",
                "SQLiteBackups");
            if (Directory.Exists(backupDir))
            {
                foreach (string bak in Directory.EnumerateFiles(backupDir, $"{dbName}_*.bak"))
                {
                    try { File.Delete(bak); } catch { }
                }
            }
        }
    }

    [Fact]
    public async Task DevAndGamerCleanerService_ScansVsDebugRetailAndWorkspaceArtifacts()
    {
        // Note: DevAndGamerCleanerService intentionally skips paths containing "FreeCleaner".
        string tempRoot = Path.Combine(Path.GetTempPath(), $"DevScanUnit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string vsProjDir = Path.Combine(tempRoot, "SampleApp");
            string binDebugDir = Path.Combine(vsProjDir, "bin", "Debug");
            string binReleaseDir = Path.Combine(vsProjDir, "bin", "Release");
            string objDir = Path.Combine(vsProjDir, "obj");
            Directory.CreateDirectory(binDebugDir);
            Directory.CreateDirectory(binReleaseDir);
            Directory.CreateDirectory(objDir);

            await File.WriteAllTextAsync(Path.Combine(vsProjDir, "SampleApp.csproj"), "<Project />");
            await File.WriteAllBytesAsync(Path.Combine(binDebugDir, "app.dll"), new byte[4096]);
            await File.WriteAllBytesAsync(Path.Combine(binReleaseDir, "app.dll"), new byte[4096]);
            await File.WriteAllBytesAsync(Path.Combine(objDir, "cache.bin"), new byte[2048]);

            string webDir = Path.Combine(tempRoot, "WebApp");
            string nodeModulesPkg = Path.Combine(webDir, "node_modules", "pkg");
            string pyCacheDir = Path.Combine(webDir, "__pycache__");
            Directory.CreateDirectory(nodeModulesPkg);
            Directory.CreateDirectory(pyCacheDir);

            await File.WriteAllBytesAsync(Path.Combine(nodeModulesPkg, "index.js"), new byte[1024]);
            await File.WriteAllBytesAsync(Path.Combine(pyCacheDir, "mod.pyc"), new byte[512]);

            List<DevArtifactFolderItem> vsArtifacts =
                await DevAndGamerCleanerService.ScanVisualStudioDebugAndRetailAsync(tempRoot);

            Assert.Contains(vsArtifacts, x => x.ArtifactType.Equals(@"VS bin\Debug", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(vsArtifacts, x => x.ArtifactType.Equals(@"VS bin\Release", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(vsArtifacts, x => x.ArtifactType.Equals("VS obj", StringComparison.OrdinalIgnoreCase));

            List<DevArtifactFolderItem> workspaceArtifacts =
                await DevAndGamerCleanerService.ScanWorkspaceArtifactsAsync(tempRoot);

            Assert.Contains(workspaceArtifacts, x => x.ArtifactType.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(workspaceArtifacts, x => x.ArtifactType.Equals("__pycache__", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void InstallerAndWingetManifests_ExistAndMatchCurrentVersion()
    {
        string repoRoot = FindRepoRoot();
        string version = AppUpdateService.CurrentVersion;

        string[] expectedFiles =
        [
            Path.Combine(repoRoot, "installer", "FreeCleaner.iss"),
            Path.Combine(repoRoot, "installer", "winget", "ErkanBahar.FreeCleanerPro.yaml"),
            Path.Combine(repoRoot, "installer", "winget", "ErkanBahar.FreeCleanerPro.installer.yaml"),
            Path.Combine(repoRoot, "installer", "winget", "ErkanBahar.FreeCleanerPro.locale.en-US.yaml")
        ];

        foreach (string path in expectedFiles)
        {
            Assert.True(File.Exists(path), $"Expected installer/manifest file missing: {path}");
            string content = File.ReadAllText(path, Encoding.UTF8);
            Assert.Contains(version, content);
        }
    }
}
