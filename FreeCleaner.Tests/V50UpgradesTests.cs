using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FreeCleaner.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FreeCleaner.Tests;

public class V50UpgradesTests
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

    private sealed class RedirectStubHttpMessageHandler(Uri finalRequestUri) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var redirectedRequest = new HttpRequestMessage(request.Method, finalRequestUri);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = redirectedRequest,
                Content = new ByteArrayContent([1, 2, 3, 4])
            };
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task SingleInstanceBridgeV50_EnforcesCurrentUserOnly_AndRejectsOversizedOrExcessiveIpcPayloads()
    {
        // Valid payload round-trip via static bounded parser
        byte[] validUtf8 = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { "--dry-run", "--profile", "SafeDaily" }));
        string[]? parsedValid = SingleInstanceBridgeService.TryParseBoundedIpcPayload(validUtf8);
        Assert.NotNull(parsedValid);
        Assert.Equal(["--dry-run", "--profile", "SafeDaily"], parsedValid!);

        // Reject >16 KiB payload
        byte[] oversized = new byte[SingleInstanceBridgeService.MaxIpcPayloadBytes + 1];
        Assert.Null(SingleInstanceBridgeService.TryParseBoundedIpcPayload(oversized));

        // Reject >32 arguments
        string[] tooManyArgs = Enumerable.Range(0, SingleInstanceBridgeService.MaxIpcArgumentCount + 1)
            .Select(i => $"--arg{i}")
            .ToArray();
        Assert.Null(SingleInstanceBridgeService.TryParseBoundedIpcPayload(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tooManyArgs))));

        // Reject >1024-char single argument
        string[] tooLongArg = [new string('x', SingleInstanceBridgeService.MaxIpcArgumentLength + 1)];
        Assert.Null(SingleInstanceBridgeService.TryParseBoundedIpcPayload(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(tooLongArg))));

        // Reject malformed JSON
        Assert.Null(SingleInstanceBridgeService.TryParseBoundedIpcPayload("{\"bad\":1}"u8));

        // Live CurrentUserOnly pipe round-trip
        string pipeName = $"FreeCleanerV50Ipc_{Guid.NewGuid():N}";
        using var bridge = new SingleInstanceBridgeService();
        var receivedTcs = new TaskCompletionSource<string[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.StartListening(args => receivedTcs.TrySetResult(args), pipeName);

        await Task.Delay(80);
        bool sent = await SingleInstanceBridgeService.SendArgsToPrimaryInstanceAsync(["--healthcheck", "--dry-run"], pipeName, timeoutMs: 2000);
        Assert.True(sent);

        string[] delivered = await receivedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(["--healthcheck", "--dry-run"], delivered);

        // Source-level invariant: CurrentUserOnly must be used on both server and client pipes
        string repoRoot = FindRepoRoot();
        string ipcCode = File.ReadAllText(Path.Combine(repoRoot, "FreeCleaner", "Services", "SingleInstanceBridgeService.cs"), Encoding.UTF8);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(ipcCode, @"PipeOptions\.CurrentUserOnly"));
    }

    [Fact]
    public void CryptoVaultV50_EvaluatesPasswordStrengthAcrossLevels_AndSupportsMaskedPasswordBoxWithAutoClear()
    {
        PasswordStrengthEvaluation empty = EighthGenAiAndSecuritySuiteService.EvaluateVaultPasswordStrength("", "en-US");
        Assert.Equal(PasswordStrengthLevel.Empty, empty.Level);
        Assert.Equal(0, empty.ScorePercent);

        PasswordStrengthEvaluation weak = EighthGenAiAndSecuritySuiteService.EvaluateVaultPasswordStrength("12345678", "tr-TR");
        Assert.Equal(PasswordStrengthLevel.Weak, weak.Level);
        Assert.Equal("Zayıf", weak.Label);

        PasswordStrengthEvaluation fair = EighthGenAiAndSecuritySuiteService.EvaluateVaultPasswordStrength("Abcd1234", "en-US");
        Assert.Equal(PasswordStrengthLevel.Fair, fair.Level);
        Assert.Equal("Fair", fair.Label);

        PasswordStrengthEvaluation strong = EighthGenAiAndSecuritySuiteService.EvaluateVaultPasswordStrength("Abcd1234!@#", "en-US");
        Assert.Equal(PasswordStrengthLevel.Strong, strong.Level);
        Assert.Equal("Strong", strong.Label);

        PasswordStrengthEvaluation veryStrong = EighthGenAiAndSecuritySuiteService.EvaluateVaultPasswordStrength("Abcd1234!@#9Wxyz", "en-US");
        Assert.Equal(PasswordStrengthLevel.VeryStrong, veryStrong.Level);
        Assert.True(veryStrong.ScorePercent >= 85);

        string repoRoot = FindRepoRoot();
        string vaultXaml = File.ReadAllText(Path.Combine(repoRoot, "FreeCleaner", "Views", "CryptoVaultView.xaml"), Encoding.UTF8);
        Assert.Contains("<PasswordBox x:Name=\"VaultPasswordBox\"", vaultXaml, StringComparison.Ordinal);
        Assert.Contains("ToggleVaultPasswordVisibility_Click", vaultXaml, StringComparison.Ordinal);
        Assert.Contains("AutoClearVaultPassword", vaultXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SqliteOptimizerV50_VerifiesIntegrityBeforeAndAfterVacuum_ReclaimsSpace_AndProtectsCorruptDatabases()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerSqliteV50_{Guid.NewGuid():N}");
        string backupDir = Path.Combine(tempRoot, "backups");
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(backupDir);

        try
        {
            string healthyDb = Path.Combine(tempRoot, "healthy.db");
            string connStr = new SqliteConnectionStringBuilder
            {
                DataSource = healthyDb,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();

            using (var setupConn = new SqliteConnection(connStr))
            {
                setupConn.Open();
                using (SqliteCommand createCmd = setupConn.CreateCommand())
                {
                    createCmd.CommandText = "CREATE TABLE Logs (Id INTEGER PRIMARY KEY, Payload TEXT);";
                    createCmd.ExecuteNonQuery();
                }

                using (SqliteTransaction tx = setupConn.BeginTransaction())
                {
                    using SqliteCommand ins = setupConn.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = "INSERT INTO Logs (Payload) VALUES ($p);";
                    SqliteParameter pParam = ins.Parameters.Add("$p", SqliteType.Text);
                    string chunk = new('A', 2048);
                    for (int i = 0; i < 150; i++)
                    {
                        pParam.Value = chunk;
                        ins.ExecuteNonQuery();
                    }
                    tx.Commit();
                }

                using (SqliteCommand delCmd = setupConn.CreateCommand())
                {
                    delCmd.CommandText = "DELETE FROM Logs;";
                    delCmd.ExecuteNonQuery();
                }

                (bool preOk, string preStatus) = NinthGenWingetAndNetworkSuiteService.VerifySqliteIntegrity(setupConn);
                Assert.True(preOk, preStatus);
            }

            long beforeBytes = new FileInfo(healthyDb).Length;
            (bool ok, long freedBytes, string msg) = NinthGenWingetAndNetworkSuiteService.OptimizeSingleSqliteDatabase(
                healthyDb,
                backupDir);

            Assert.True(ok, msg);
            Assert.True(freedBytes > 0, $"Expected freedBytes > 0 after VACUUM, got {freedBytes}");
            Assert.True(new FileInfo(healthyDb).Length < beforeBytes);
            Assert.NotEmpty(Directory.GetFiles(backupDir, "*.bak"));

            // Corrupt DB file must fail pre-VACUUM quick_check cleanly without data loss
            string corruptDb = Path.Combine(tempRoot, "corrupt.db");
            byte[] corruptPayload = Encoding.ASCII.GetBytes("NOT A VALID SQLITE DATABASE HEADER PAYLOAD FOR TEST");
            File.WriteAllBytes(corruptDb, corruptPayload);

            (bool corruptOk, long corruptFreed, string corruptMsg) = NinthGenWingetAndNetworkSuiteService.OptimizeSingleSqliteDatabase(
                corruptDb,
                backupDir);

            Assert.False(corruptOk);
            Assert.Equal(0, corruptFreed);
            Assert.False(string.IsNullOrWhiteSpace(corruptMsg));
            Assert.Equal(corruptPayload, File.ReadAllBytes(corruptDb));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failure.
            }
        }
    }

    [Fact]
    public async Task AppUpdateServiceV50_BlocksUntrustedRedirectHost_GuardsZipSlip_AndCleansUpStaleOldBinary()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerUpdV50_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            string targetExe = Path.Combine(tempRoot, "FreeCleaner.exe");
            string staleOld = targetExe + ".old";
            await File.WriteAllTextAsync(staleOld, "stale-old-binary", Encoding.UTF8);
            Assert.True(File.Exists(staleOld));

            Assert.True(AppUpdateService.TryCleanupStaleUpdateBackup(targetExe));
            Assert.False(File.Exists(staleOld));
            Assert.False(AppUpdateService.TryCleanupStaleUpdateBackup(targetExe));

            // Redirect to untrusted third-party host must abort cleanly
            using var stubHandler = new RedirectStubHttpMessageHandler(new Uri("https://evil.example.com/FreeCleaner.exe"));
            using var httpClient = new HttpClient(stubHandler);
            string destExe = Path.Combine(tempRoot, "downloaded.exe");

            UpdateDownloadResult res = await AppUpdateService.DownloadAndVerifyUpdateAsync(
                "https://github.com/erkanbahar/FreeCleaner/releases/download/v5.0.0/FreeCleaner.exe",
                destExe,
                expectedSha256: null,
                httpClient: httpClient,
                requireHash: false);

            Assert.False(res.Success);
            Assert.Contains("evil.example.com", res.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(destExe));

            // Zip-Slip path traversal protection in generated self-update PowerShell script
            string script = AppUpdateService.BuildSafeSelfUpdateScript(99999, targetExe, Path.Combine(tempRoot, "pkg.zip"));
            Assert.Contains("Zip-Slip", script, StringComparison.Ordinal);
            Assert.Contains("StartsWith($extractRoot", script, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Ignore temp cleanup failure.
            }
        }
    }

    [Fact]
    public void Architecture_V50_All57ViewsHaveAutomationProperties_AndAppXamlEnforcesGlobalDataGridVirtualization()
    {
        string repoRoot = FindRepoRoot();
        string viewsDir = Path.Combine(repoRoot, "FreeCleaner", "Views");
        string[] viewFiles = Directory.GetFiles(viewsDir, "*.xaml", SearchOption.TopDirectoryOnly);
        Assert.Equal(57, viewFiles.Length);

        foreach (string viewPath in viewFiles)
        {
            string xaml = File.ReadAllText(viewPath, Encoding.UTF8);
            Assert.True(
                xaml.Contains("AutomationProperties.Name=", StringComparison.Ordinal),
                $"View '{Path.GetFileName(viewPath)}' is missing AutomationProperties.Name.");
        }

        string appXaml = File.ReadAllText(Path.Combine(repoRoot, "FreeCleaner", "App.xaml"), Encoding.UTF8);
        Assert.Contains("<Style TargetType=\"DataGrid\">", appXaml, StringComparison.Ordinal);
        Assert.Contains("EnableRowVirtualization", appXaml, StringComparison.Ordinal);
        Assert.Contains("VirtualizationMode", appXaml, StringComparison.Ordinal);
        Assert.Contains("Recycling", appXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Architecture_V50_CiCoverageFloorRaisedTo36_AndReleaseConfiguredForVersion500()
    {
        string repoRoot = FindRepoRoot();
        Assert.Equal("5.1.0", AppUpdateService.CurrentVersion);

        string ciYml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "dotnet-ci.yml"), Encoding.UTF8);
        var floorMatch = System.Text.RegularExpressions.Regex.Match(ciYml, @"\$minLineCoverage\s*=\s*(\d+(?:\.\d+)?)");
        Assert.True(floorMatch.Success);
        double floor = double.Parse(floorMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(floor >= 36.0, $"Expected CI coverage floor >= 36.0, got {floor}");

        string releaseYml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"), Encoding.UTF8);
        Assert.Contains("v5.1.0", releaseYml, StringComparison.Ordinal);
    }
}
