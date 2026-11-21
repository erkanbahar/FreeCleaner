using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace FreeCleaner.Services;

public sealed record UpdateCheckResult(
    bool Success,
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string ReleaseUrl,
    string ReleaseNotes,
    string StatusMessage);

/// <summary>
/// Checks the latest published release on GitHub and compares semantic versions safely.
/// </summary>
public static class AppUpdateService
{
    public const string CurrentVersion = "4.0.0";
    public const string ReleasesApiUrl = "https://api.github.com/repos/erkanbahar/FreeCleaner/releases/latest";
    public const string ReleasesPageUrl = "https://github.com/erkanbahar/FreeCleaner/releases";

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
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
                StatusMessage: msg);
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
}
