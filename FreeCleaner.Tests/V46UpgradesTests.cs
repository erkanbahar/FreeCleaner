using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FreeCleaner.Services;
using FreeCleaner.ViewModels;
using Xunit;

namespace FreeCleaner.Tests;

public class V46UpgradesTests
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
    public async Task AsyncRelayCommand_ExecutesAsyncTask_HandlesExceptionsSafely_AndPreventsReentrancy()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int runCount = 0;
        Exception? captured = null;

        var cmd = new AsyncRelayCommand(
            async _ =>
            {
                runCount++;
                await gate.Task;
            },
            onError: ex => captured = ex);

        Assert.True(cmd.CanExecute(null));
        Task firstRun = cmd.ExecuteAsync(null);
        Assert.False(cmd.CanExecute(null));
        await cmd.ExecuteAsync(null); // Re-entrant call while busy must be ignored
        Assert.Equal(1, runCount);

        gate.SetResult();
        await firstRun;
        Assert.True(cmd.CanExecute(null));
        Assert.Null(captured);

        // Exceptions inside async body must flow cleanly to onError without throwing out of Execute
        var failingCmd = new AsyncRelayCommand(
            async _ =>
            {
                await Task.Yield();
                throw new InvalidOperationException("Simulated command failure");
            },
            onError: ex => captured = ex);

        await failingCmd.ExecuteAsync(null);
        Assert.NotNull(captured);
        Assert.Equal("Simulated command failure", captured!.Message);

        // OperationCanceledException is treated as clean cancellation, not an unhandled error
        Exception? cancelCaptured = null;
        var cancellingCmd = new AsyncRelayCommand(
            async _ =>
            {
                await Task.Yield();
                throw new OperationCanceledException();
            },
            onError: ex => cancelCaptured = ex);

        await cancellingCmd.ExecuteAsync(null);
        Assert.Null(cancelCaptured);
    }

    [Fact]
    public void SafeProcessRunner_NeutralizesShellMetacharacters_AndBuildsEncodedCommands()
    {
        // 1. Direct ProcessStartInfo isolates adversarial Wi-Fi SSID into a single ArgumentList token
        string maliciousSsid = "Coffee & calc.exe | whoami; \"quoted\"\r\n";
        ProcessStartInfo netshPsi = SafeProcessRunner.BuildDirectProcessStartInfo(
            "netsh.exe",
            ["wlan", "show", "profile", $"name={maliciousSsid}", "key=clear"],
            redirectOutput: true);

        Assert.False(netshPsi.UseShellExecute);
        Assert.Equal("netsh.exe", netshPsi.FileName);
        Assert.Equal(5, netshPsi.ArgumentList.Count);
        Assert.Contains("Coffee & calc.exe | whoami", netshPsi.ArgumentList[3]);
        Assert.DoesNotContain("\r", netshPsi.ArgumentList[3]);
        Assert.DoesNotContain("\n", netshPsi.ArgumentList[3]);

        // 2. QuotePowerShellSingle doubles single quotes so $(), & | ; remain literal strings
        string quoted = SafeProcessRunner.QuotePowerShellSingle("O'Brien & $(calc)");
        Assert.Equal("'O''Brien & $(calc)'", quoted);

        // 3. BuildEncodedPowerShellStartInfo uses -EncodedCommand UTF-16LE round-trip without raw shell interpolation
        string firewallScript = SafeProcessRunner.BuildFirewallBlockScript(
            "FreeCleaner_Block_Bad&App",
            @"C:\Users\Test'User\My & App\evil$(whoami).exe");

        ProcessStartInfo elevatedPsi = SafeProcessRunner.BuildEncodedPowerShellStartInfo(firewallScript, runAsAdmin: true);
        Assert.True(elevatedPsi.UseShellExecute);
        Assert.Equal("runas", elevatedPsi.Verb);
        Assert.Contains("-EncodedCommand ", elevatedPsi.Arguments);
        Assert.DoesNotContain("whoami", elevatedPsi.Arguments);

        string base64 = elevatedPsi.Arguments.Split("-EncodedCommand ", StringSplitOptions.TrimEntries)[1];
        string decodedScript = Encoding.Unicode.GetString(Convert.FromBase64String(base64));
        Assert.Contains("'FreeCleaner_Block_Bad&App'", decodedScript);
        Assert.Contains("'C:\\Users\\Test''User\\My & App\\evil$(whoami).exe'", decodedScript);

        // 4. Delete, Mount, Dismount, Appx script builders safely quote adversarial inputs
        string deleteScript = SafeProcessRunner.BuildFirewallDeleteScript(["Rule'A & calc", "  "]);
        Assert.Contains("'Rule''A & calc'", deleteScript);

        string mountScript = SafeProcessRunner.BuildMountDiskImageScript(@"D:\ISOs\Win'11 & Dev.iso");
        Assert.Contains("'D:\\ISOs\\Win''11 & Dev.iso'", mountScript);

        string dismountDriveScript = SafeProcessRunner.BuildDismountDiskImageScript(string.Empty, 'e');
        Assert.Contains("'E'", dismountDriveScript);

        string removeAppxScript = SafeProcessRunner.BuildRemoveAppxScript("Corp.App'$(1)");
        Assert.Contains("'Corp.App''$(1)'", removeAppxScript);
    }

    [Fact]
    public void AppStoragePathsService_ReportsFontBackupsAndWorkspaceRoot_RespectPortableModeAndAvoidDevPaths()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), $"FreeCleanerV46Port_{Guid.NewGuid():N}");
        string preferredDir = Path.Combine(tempRoot, "Projects");
        string missingDir = Path.Combine(tempRoot, "DoesNotExist");
        Directory.CreateDirectory(preferredDir);

        try
        {
            // Standard mode resolves to Desktop subfolders
            Assert.False(AppStoragePathsService.IsPortableModeEnabled(tempRoot));
            Assert.EndsWith("FreeCleaner_Raporlar", AppStoragePathsService.GetReportsDirectory(tempRoot), StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("FreeCleaner_FontBackup", AppStoragePathsService.GetFontBackupsDirectory(tempRoot), StringComparison.OrdinalIgnoreCase);

            // Portable mode resolves strictly inside ./data without touching Desktop
            File.WriteAllText(AppStoragePathsService.GetPortableFlagPath(tempRoot), "portable");
            Assert.Equal(Path.Combine(tempRoot, "data", "Reports"), AppStoragePathsService.GetReportsDirectory(tempRoot));
            Assert.Equal(Path.Combine(tempRoot, "data", "FontBackups"), AppStoragePathsService.GetFontBackupsDirectory(tempRoot));

            // Workspace resolver picks first existing candidate
            string resolved = AppStoragePathsService.ResolveDefaultDevWorkspaceRoot([missingDir, preferredDir]);
            Assert.Equal(preferredDir, resolved);

            string fallback = AppStoragePathsService.ResolveDefaultDevWorkspaceRoot([missingDir]);
            Assert.True(Directory.Exists(fallback));

            // Public default rule list must not contain hardcoded developer drive path
            var vsRule = Assert.Single(CleanerEngine.GetDefaultRules(), r => r.Id == "app_vs_debug_retail");
            Assert.DoesNotContain(@"C:\CURSOR", vsRule.Description, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"C:\CURSOR", LocalizationService.CurrentLanguage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Architecture_ZeroAsyncVoidRelayCommands_AndZeroHardcodedCursorPaths()
    {
        string repoRoot = FindRepoRoot();
        string vmDir = Path.Combine(repoRoot, "FreeCleaner", "ViewModels");
        string srcRoot = Path.Combine(repoRoot, "FreeCleaner");

        var asyncRelayRegex = new Regex(@"new\s+RelayCommand\s*\(\s*async\b", RegexOptions.Compiled);
        foreach (string vmFile in Directory.GetFiles(vmDir, "*.cs", SearchOption.AllDirectories))
        {
            string code = File.ReadAllText(vmFile, Encoding.UTF8);
            Assert.False(
                asyncRelayRegex.IsMatch(code),
                $"Legacy async void 'new RelayCommand(async ...)' found in {Path.GetFileName(vmFile)}. Use CreateAsyncCommand instead.");
        }

        foreach (string csFile in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (csFile.Contains(@"\obj\", StringComparison.OrdinalIgnoreCase) ||
                csFile.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string code = File.ReadAllText(csFile, Encoding.UTF8);
            Assert.DoesNotContain(@"C:\CURSOR", code, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(@"C:\\CURSOR", code, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ReleasePipelineAndPublishScript_SupportReadyToRunAndVersion480()
    {
        string repoRoot = FindRepoRoot();
        string version = AppUpdateService.CurrentVersion;
        Assert.Equal("4.8.0", version);

        string publishScript = File.ReadAllText(Path.Combine(repoRoot, "scripts", "Publish-Portable.ps1"), Encoding.UTF8);
        Assert.Contains("PublishReadyToRun=true", publishScript);
        Assert.Contains("$GenerateWingetManifests", publishScript);

        string releaseYml = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "release.yml"), Encoding.UTF8);
        Assert.Contains("-p:PublishReadyToRun=true", releaseYml);
        Assert.Contains($"v{version}", releaseYml);
    }
}
