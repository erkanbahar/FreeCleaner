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

    private static string ResolveAssemblyVersion()
    {
        Version? v = typeof(AppUpdateService).Assembly.GetName().Version;
        if (v is null)
            return "4.6.0";

        return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
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
                    StatusMessage: $"Güncelsiniz (v{CurrentVersion}) — Henüz yayınlanmış GitHub Release bulunamadı.");
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
                    StatusMessage: $"Güncelleme sunucusu yanıt vermedi (HTTP {(int)response.StatusCode}).");
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
                ? $"🚀 Yeni sürüm mevcut: {latestTag} (Mevcut: v{CurrentVersion})"
                : $"✅ En güncel sürümü kullanıyorsunuz (v{CurrentVersion}).";

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
                StatusMessage: "Güncelleme kontrolü iptal edildi.");
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(
                Success: false,
                IsUpdateAvailable: false,
                CurrentVersion: CurrentVersion,
                LatestVersion: CurrentVersion,
                ReleaseUrl: ReleasesPageUrl,
                ReleaseNotes: string.Empty,
                StatusMessage: $"Bağlantı hatası: {ex.Message}");
        }
    }

    private static async Task<(string? AssetUrl, string? AssetName, string? ExpectedSha, string? ChecksumsUrl)>
        ExtractPortableAssetInfoAsync(JsonElement root, CancellationToken ct)
    {
        if (!root.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
            return (null, null, null, null);

        string? exeUrl = null, exeName = null, exeSha = null;
        string? zipUrl = null, zipName = null, zipSha = null;
        string? sumsUrl = null;

        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? string.Empty : string.Empty;
            string url = asset.TryGetProperty("browser_download_url", out var uProp) ? uProp.GetString() ?? string.Empty : string.Empty;
            string? digest = asset.TryGetProperty("digest", out var dProp) ? dProp.GetString() : null;
            string? parsedDigest = NormalizeDigest(digest);

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url))
                continue;

            if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
            {
                sumsUrl = url;
            }
            else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                exeUrl = url;
                exeName = name;
                exeSha = parsedDigest;
            }
            else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                zipUrl = url;
                zipName = name;
                zipSha = parsedDigest;
            }
        }

        string? chosenUrl = exeUrl ?? zipUrl;
        string? chosenName = exeName ?? zipName;
        string? chosenSha = exeUrl is not null ? exeSha : zipSha;

        if (chosenSha is null && chosenName is not null && !string.IsNullOrWhiteSpace(sumsUrl))
        {
            try
            {
                string sumsText = await SharedHttpClient.GetStringAsync(sumsUrl, ct);
                chosenSha = ParseSha256SumsText(sumsText, chosenName);
            }
            catch
            {
                // Optional checksum download failure leaves hash null.
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
        HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

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
                    $"Güncelleme dosyası indirilemedi (HTTP {(int)response.StatusCode}).");
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
                        $"Güvenlik uyarısı: SHA-256 doğrulaması başarısız oldu (beklenen: {expectedSha256[..12]}..., indirilen: {computed[..12]}...). İndirilen dosya silindi.");
                }

                return new UpdateDownloadResult(true, destinationPath, computed, true, "Güncelleme indirildi ve SHA-256 doğrulaması başarılı.");
            }

            return new UpdateDownloadResult(true, destinationPath, null, false, "Güncelleme dosyası indirildi (yayında SHA-256 özeti yer almadığı için hash atlandı).");
        }
        catch (OperationCanceledException)
        {
            try { if (File.Exists(destinationPath)) File.Delete(destinationPath); } catch { }
            return new UpdateDownloadResult(false, null, null, false, "Güncelleme indirme işlemi iptal edildi.");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(destinationPath)) File.Delete(destinationPath); } catch { }
            return new UpdateDownloadResult(false, null, null, false, $"İndirme hatası: {ex.Message}");
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
