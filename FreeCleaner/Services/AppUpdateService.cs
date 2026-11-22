using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FreeCleaner.Services;

public sealed record UpdateCheckResult(
    bool Success,
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string ReleaseNotes,
    string StatusMessage,
    string? DownloadAssetUrl = null,
    string? DownloadAssetName = null,
    string? ExpectedSha256 = null,
    string? ChecksumsAssetUrl = null);

public sealed record UpdateDownloadResult(
    bool Success,
    string? FilePath,
    string? ComputedSha256,
    bool HashVerified,
    string Message);

/// <summary>
/// Checks the latest published release on GitHub, downloads portable assets,
/// verifies SHA-256 checksums, and generates an atomic self-update PowerShell script.
/// </summary>
public static class AppUpdateService
{
    public static string CurrentVersion { get; } = ResolveAssemblyVersion();
    public const string ReleasesApiUrl = "https://api.github.com/repos/erkanbahar/FreeCleaner/releases/latest";
    public const string ReleasesPageUrl = "https://github.com/erkanbahar/FreeCleaner/releases";

    private static readonly HashSet<string> TrustedReleaseHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "api.github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "raw.githubusercontent.com"
    };

    private static string ResolveAssemblyVersion()
    {
        Version? v = typeof(AppUpdateService).Assembly.GetName().Version;
        if (v is null)
            return "4.8.0";

        return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
    }

    public static bool IsTrustedDownloadUri(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return false;

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out Uri? uri))
            return false;

        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               TrustedReleaseHosts.Contains(uri.Host);
    }

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(3)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"FreeCleanerPro/{CurrentVersion}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public static bool IsNewerVersion(string currentVersion, string? latestTag)
    {
        if (!TryNormalizeVersion(currentVersion, out Version? current) ||
            !TryNormalizeVersion(latestTag, out Version? latest))
        {
            return false;
        }

        return latest > current;
    }

    private static bool TryNormalizeVersion(string? raw, out Version? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        string clean = raw.Trim();
        if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            clean = clean[1..];

        int dashIdx = clean.IndexOf('-');
        if (dashIdx >= 0)
            clean = clean[..dashIdx];

        int plusIdx = clean.IndexOf('+');
        if (plusIdx >= 0)
            clean = clean[..plusIdx];

        string[] parts = clean.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
            clean += ".0";
        else if (parts.Length == 1 && int.TryParse(parts[0], out _))
            clean += ".0.0";

        return Version.TryParse(clean, out parsed);
    }

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await SharedHttpClient.GetAsync(ReleasesApiUrl, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(
                    Success: true,
                    IsUpdateAvailable: false,
                    CurrentVersion: CurrentVersion,
                    LatestVersion: CurrentVersion,
                    ReleaseUrl: ReleasesPageUrl,
                    ReleaseNotes: string.Empty,
                    StatusMessage: LocalizationService.GetText(
                        $"Güncelsiniz (v{CurrentVersion}) — Henüz yayınlanmış GitHub Release bulunamadı.",
                        $"You are up to date (v{CurrentVersion}) — No published GitHub Release found yet."));
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(
                    Success: false,
                    IsUpdateAvailable: false,
                    CurrentVersion: CurrentVersion,
                    LatestVersion: CurrentVersion,
                    ReleaseUrl: ReleasesPageUrl,
                    ReleaseNotes: string.Empty,
                    StatusMessage: LocalizationService.GetText(
                        $"Güncelleme sunucusu yanıt vermedi (HTTP {(int)response.StatusCode}).",
                        $"Update server did not respond (HTTP {(int)response.StatusCode})."));
            }

            string json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            string latestTag = root.TryGetProperty("tag_name", out var tagProp)
                ? tagProp.GetString() ?? CurrentVersion
                : CurrentVersion;

            string releaseUrl = root.TryGetProperty("html_url", out var urlProp)
                ? urlProp.GetString() ?? ReleasesPageUrl
                : ReleasesPageUrl;

            string body = root.TryGetProperty("body", out var bodyProp)
                ? bodyProp.GetString() ?? string.Empty
                : string.Empty;

            (string? assetUrl, string? assetName, string? expectedSha, string? sumsUrl) =
                await ExtractPortableAssetInfoAsync(root, ct);

            bool hasUpdate = IsNewerVersion(CurrentVersion, latestTag);
            string msg = hasUpdate
                ? LocalizationService.GetText(
                    $"🚀 Yeni sürüm mevcut: {latestTag} (Mevcut: v{CurrentVersion})",
                    $"🚀 New release available: {latestTag} (Current: v{CurrentVersion})")
                : LocalizationService.GetText(
                    $"✅ En güncel sürümü kullanıyorsunuz (v{CurrentVersion}).",
                    $"✅ You are running the latest version (v{CurrentVersion}).");

            return new UpdateCheckResult(
                Success: true,
                IsUpdateAvailable: hasUpdate,
                CurrentVersion: CurrentVersion,
                LatestVersion: latestTag,
                ReleaseUrl: releaseUrl,
                ReleaseNotes: body,
                StatusMessage: msg,
                DownloadAssetUrl: assetUrl,
                DownloadAssetName: assetName,
                ExpectedSha256: expectedSha,
                ChecksumsAssetUrl: sumsUrl);
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult(
                Success: false,
                IsUpdateAvailable: false,
                CurrentVersion: CurrentVersion,
                LatestVersion: CurrentVersion,
                ReleaseUrl: ReleasesPageUrl,
                ReleaseNotes: string.Empty,
                StatusMessage: LocalizationService.GetText(
                    "Güncelleme kontrolü iptal edildi.",
                    "Update check cancelled."));
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("AppUpdate:Check", ex);
            return new UpdateCheckResult(
                Success: false,
                IsUpdateAvailable: false,
                CurrentVersion: CurrentVersion,
                LatestVersion: CurrentVersion,
                ReleaseUrl: ReleasesPageUrl,
                ReleaseNotes: string.Empty,
                StatusMessage: LocalizationService.GetText(
                    $"Bağlantı hatası: {ex.Message}",
                    $"Connection error: {ex.Message}"));
        }
    }

    public static (string? AssetUrl, string? AssetName, string? ExpectedSha, string? ChecksumsUrl)
        SelectPortableReleaseAsset(JsonElement root, bool preferUsbPortable = false)
    {
        if (!root.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
            return (null, null, null, null);

        string? tier1ExeUrl = null, tier1ExeName = null, tier1ExeSha = null;
        string? portableUsbZipUrl = null, portableUsbZipName = null, portableUsbZipSha = null;
        string? portableStdZipUrl = null, portableStdZipName = null, portableStdZipSha = null;
        string? fallbackExeUrl = null, fallbackExeName = null, fallbackExeSha = null;
        string? fallbackZipUrl = null, fallbackZipName = null, fallbackZipSha = null;
        string? sumsUrl = null;

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? string.Empty : string.Empty;
            string url = asset.TryGetProperty("browser_download_url", out var uProp) ? uProp.GetString() ?? string.Empty : string.Empty;
            string? digest = asset.TryGetProperty("digest", out var dProp) ? dProp.GetString() : null;
            string? parsedDigest = NormalizeDigest(digest);

            if (string.IsNullOrWhiteSpace(name) || !IsTrustedDownloadUri(url))
                continue;

            if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
            {
                sumsUrl ??= url;
                continue;
            }

            if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Winget", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Manifest", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.Equals("FreeCleaner.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (tier1ExeUrl is null)
                {
                    tier1ExeUrl = url;
                    tier1ExeName = name;
                    tier1ExeSha = parsedDigest;
                }
            }
            else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                     name.Contains("Portable", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Contains("-USB-", StringComparison.OrdinalIgnoreCase))
                {
                    if (portableUsbZipUrl is null)
                    {
                        portableUsbZipUrl = url;
                        portableUsbZipName = name;
                        portableUsbZipSha = parsedDigest;
                    }
                }
                else if (portableStdZipUrl is null)
                {
                    portableStdZipUrl = url;
                    portableStdZipName = name;
                    portableStdZipSha = parsedDigest;
                }
            }
            else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (fallbackExeUrl is null)
                {
                    fallbackExeUrl = url;
                    fallbackExeName = name;
                    fallbackExeSha = parsedDigest;
                }
            }
            else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (fallbackZipUrl is null)
                {
                    fallbackZipUrl = url;
                    fallbackZipName = name;
                    fallbackZipSha = parsedDigest;
                }
            }
        }

        if (tier1ExeUrl is not null)
            return (tier1ExeUrl, tier1ExeName, tier1ExeSha, sumsUrl);

        if (preferUsbPortable && portableUsbZipUrl is not null)
            return (portableUsbZipUrl, portableUsbZipName, portableUsbZipSha, sumsUrl);

        if (portableStdZipUrl is not null)
            return (portableStdZipUrl, portableStdZipName, portableStdZipSha, sumsUrl);

        if (portableUsbZipUrl is not null)
            return (portableUsbZipUrl, portableUsbZipName, portableUsbZipSha, sumsUrl);

        if (fallbackExeUrl is not null)
            return (fallbackExeUrl, fallbackExeName, fallbackExeSha, sumsUrl);

        return (fallbackZipUrl, fallbackZipName, fallbackZipSha, sumsUrl);
    }

    private static async Task<(string? AssetUrl, string? AssetName, string? ExpectedSha, string? ChecksumsUrl)>
        ExtractPortableAssetInfoAsync(JsonElement root, CancellationToken ct)
    {
        (string? chosenUrl, string? chosenName, string? chosenSha, string? sumsUrl) =
            SelectPortableReleaseAsset(root, AppStoragePathsService.IsPortableModeEnabled());

        if (chosenSha is null && chosenName is not null && IsTrustedDownloadUri(sumsUrl))
        {
            try
            {
                string sumsText = await SharedHttpClient.GetStringAsync(sumsUrl, ct);
                chosenSha = ParseSha256SumsText(sumsText, chosenName);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ErrorLogService.Log("AppUpdate:Checksums", ex);
            }
        }

        return (chosenUrl, chosenName, chosenSha, sumsUrl);
    }

    private static string? NormalizeDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
            return null;

        string trimmed = digest.Trim();
        if (trimmed.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[7..].Trim();

        return IsValidSha256Hex(trimmed) ? trimmed.ToLowerInvariant() : null;
    }

    public static string? ParseSha256SumsText(string? checksumsText, string targetFileName)
    {
        if (string.IsNullOrWhiteSpace(checksumsText) || string.IsNullOrWhiteSpace(targetFileName))
            return null;

        string targetLeaf = Path.GetFileName(targetFileName);
        foreach (string rawLine in checksumsText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length < 65 || line.StartsWith('#'))
                continue;

            string[] tokens = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
                continue;

            string candidateHash = tokens[0].Trim();
            string filePart = tokens[^1].Trim().TrimStart('*');
            string fileLeaf = Path.GetFileName(filePart);

            if (IsValidSha256Hex(candidateHash) &&
                (filePart.Equals(targetFileName, StringComparison.OrdinalIgnoreCase) ||
                 fileLeaf.Equals(targetLeaf, StringComparison.OrdinalIgnoreCase)))
            {
                return candidateHash.ToLowerInvariant();
            }
        }

        return null;
    }

    public static async Task<(bool Matches, string ComputedSha256)> VerifyFileSha256Async(
        string filePath,
        string expectedSha256,
        CancellationToken ct = default)
    {
        await using var fs = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 131072,
            useAsync: true);

        byte[] hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        string computed = Convert.ToHexString(hash).ToLowerInvariant();
        string normalizedExpected = expectedSha256.Trim().ToLowerInvariant();
        return (string.Equals(computed, normalizedExpected, StringComparison.OrdinalIgnoreCase), computed);
    }

    public static async Task<UpdateDownloadResult> DownloadAndVerifyUpdateAsync(
        string downloadUrl,
        string destinationPath,
        string? expectedSha256,
        IProgress<int>? progress = null,
        CancellationToken ct = default,
        HttpClient? httpClient = null,
        bool requireHash = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (!IsTrustedDownloadUri(downloadUrl))
        {
            return new UpdateDownloadResult(
                false,
                null,
                null,
                false,
                LocalizationService.GetText(
                    "Güvenlik uyarısı: İndirme bağlantısı güvenilir GitHub HTTPS alan adı dışında olduğu için reddedildi.",
                    "Security warning: Download URL was rejected because it is not an HTTPS GitHub release host."));
        }

        if (requireHash && string.IsNullOrWhiteSpace(expectedSha256))
        {
            return new UpdateDownloadResult(
                false,
                null,
                null,
                false,
                LocalizationService.GetText(
                    "Güvenlik uyarısı: Bu yayında SHA-256 doğrulama özeti zorunludur ancak alınamadı. İndirme iptal edildi.",
                    "Security warning: A SHA-256 checksum is required for this release but could not be retrieved. Download aborted."));
        }

        HttpClient client = httpClient ?? SharedHttpClient;
        string? parent = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        try
        {
            using HttpResponseMessage response = await client.GetAsync(
                downloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateDownloadResult(
                    false,
                    null,
                    null,
                    false,
                    LocalizationService.GetText(
                        $"Güncelleme dosyası indirilemedi (HTTP {(int)response.StatusCode}).",
                        $"Failed to download update package (HTTP {(int)response.StatusCode})."));
            }

            long? totalBytes = response.Content.Headers.ContentLength;
            await using (Stream remoteStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var localFs = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                byte[] buffer = new byte[81920];
                long received = 0;
                int lastReportedPct = -1;

                while (true)
                {
                    int read = await remoteStream.ReadAsync(buffer, ct).ConfigureAwait(false);
                    if (read <= 0)
                        break;

                    await localFs.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    received += read;

                    if (totalBytes is > 0 && progress is not null)
                    {
                        int pct = (int)Math.Clamp((received * 100L) / totalBytes.Value, 0, 100);
                        if (pct != lastReportedPct)
                        {
                            lastReportedPct = pct;
                            progress.Report(pct);
                        }
                    }
                }
            }

            progress?.Report(100);

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var (matches, computed) = await VerifyFileSha256Async(destinationPath, expectedSha256, ct).ConfigureAwait(false);
                if (!matches)
                {
                    try { File.Delete(destinationPath); } catch { }
                    return new UpdateDownloadResult(
                        false,
                        null,
                        computed,
                        false,
                        LocalizationService.GetText(
                            $"Güvenlik uyarısı: SHA-256 doğrulaması başarısız oldu (beklenen: {expectedSha256[..12]}..., indirilen: {computed[..12]}...). İndirilen dosya silindi.",
                            $"Security warning: SHA-256 verification failed (expected: {expectedSha256[..12]}..., downloaded: {computed[..12]}...). Downloaded file deleted."));
                }

                return new UpdateDownloadResult(
                    true,
                    destinationPath,
                    computed,
                    true,
                    LocalizationService.GetText(
                        "Güncelleme indirildi ve SHA-256 doğrulaması başarılı.",
                        "Update downloaded and SHA-256 verification succeeded."));
            }

            return new UpdateDownloadResult(
                true,
                destinationPath,
                null,
                false,
                LocalizationService.GetText(
                    "Güncelleme dosyası indirildi (yayında SHA-256 özeti yer almadığı için hash atlandı).",
                    "Update file downloaded (SHA-256 hash skipped because release did not include a digest)."));
        }
        catch (OperationCanceledException)
        {
            try { if (File.Exists(destinationPath)) File.Delete(destinationPath); } catch { }
            return new UpdateDownloadResult(
                false,
                null,
                null,
                false,
                LocalizationService.GetText(
                    "Güncelleme indirme işlemi iptal edildi.",
                    "Update download was cancelled."));
        }
        catch (Exception ex)
        {
            ErrorLogService.Log("AppUpdate:Download", ex);
            try { if (File.Exists(destinationPath)) File.Delete(destinationPath); } catch { }
            return new UpdateDownloadResult(
                false,
                null,
                null,
                false,
                LocalizationService.GetText(
                    $"İndirme hatası: {ex.Message}",
                    $"Download error: {ex.Message}"));
        }
    }

    public static string BuildSafeSelfUpdateScript(int currentPid, string currentExePath, string downloadedFilePath)
    {
        string escapedCurrentExe = EscapePsSingleQuoted(Path.GetFullPath(currentExePath));
        string escapedDownloaded = EscapePsSingleQuoted(Path.GetFullPath(downloadedFilePath));
        bool isZip = downloadedFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine($"$targetPid = {currentPid}");
        sb.AppendLine($"$currentExe = '{escapedCurrentExe}'");
        sb.AppendLine($"$downloaded = '{escapedDownloaded}'");
        sb.AppendLine("$backupExe = \"$currentExe.old\"");
        sb.AppendLine("for ($i = 0; $i -lt 40; $i++) {");
        sb.AppendLine("    if (-not (Get-Process -Id $targetPid -ErrorAction SilentlyContinue)) { break }");
        sb.AppendLine("    Start-Sleep -Milliseconds 250");
        sb.AppendLine("}");

        if (isZip)
        {
            sb.AppendLine("$extractDir = Join-Path ([System.IO.Path]::GetDirectoryName($downloaded)) 'extracted'");
            sb.AppendLine("if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }");
            sb.AppendLine("Expand-Archive -Path $downloaded -DestinationPath $extractDir -Force");
            sb.AppendLine("$newExeObj = Get-ChildItem -Path $extractDir -Filter 'FreeCleaner.exe' -Recurse | Select-Object -First 1");
            sb.AppendLine("if (-not $newExeObj) { throw 'FreeCleaner.exe could not be located inside the downloaded zip.' }");
            sb.AppendLine("$newExe = $newExeObj.FullName");
        }
        else
        {
            sb.AppendLine("$newExe = $downloaded");
        }

        sb.AppendLine("try {");
        sb.AppendLine("    if (Test-Path $backupExe) { Remove-Item $backupExe -Force }");
        sb.AppendLine("    if (Test-Path $currentExe) { Move-Item -Path $currentExe -Destination $backupExe -Force }");
        sb.AppendLine("    Copy-Item -Path $newExe -Destination $currentExe -Force");
        sb.AppendLine("    Start-Process -FilePath $currentExe");
        sb.AppendLine("} catch {");
        sb.AppendLine("    if (-not (Test-Path $currentExe) -and (Test-Path $backupExe)) {");
        sb.AppendLine("        Move-Item -Path $backupExe -Destination $currentExe -Force");
        sb.AppendLine("    }");
        sb.AppendLine("    throw");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string EscapePsSingleQuoted(string input) => input.Replace("'", "''", StringComparison.Ordinal);

    private static bool IsValidSha256Hex(string value)
    {
        if (value.Length != 64)
            return false;

        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }
        return true;
    }
}
