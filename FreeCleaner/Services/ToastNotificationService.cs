using System.Globalization;

namespace FreeCleaner.Services;

public enum ToastSeverity
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record ToastNotificationItem(
    string Id,
    string Title,
    string Message,
    ToastSeverity Severity,
    string? ActionLabel = null,
    string? ActionKey = null)
{
    public bool HasAction => !string.IsNullOrWhiteSpace(ActionLabel) && !string.IsNullOrWhiteSpace(ActionKey);

    public string SeverityIcon => Severity switch
    {
        ToastSeverity.Success => "✅",
        ToastSeverity.Warning => "⚠️",
        ToastSeverity.Error => "❌",
        _ => "ℹ️"
    };
}

/// <summary>
/// Builds lightweight in-app toast notifications and evaluates daily silent update-check gating.
/// </summary>
public static class ToastNotificationService
{
    public static ToastNotificationItem BuildToast(
        string title,
        string message,
        ToastSeverity severity = ToastSeverity.Info,
        string? actionLabel = null,
        string? actionKey = null)
    {
        return new ToastNotificationItem(
            Id: Guid.NewGuid().ToString("N"),
            Title: title?.Trim() ?? string.Empty,
            Message: message?.Trim() ?? string.Empty,
            Severity: severity,
            ActionLabel: string.IsNullOrWhiteSpace(actionLabel) ? null : actionLabel.Trim(),
            ActionKey: string.IsNullOrWhiteSpace(actionKey) ? null : actionKey.Trim());
    }

    public static string GetTodayDateKey(DateTime? nowOverride = null)
        => (nowOverride ?? DateTime.Now).Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static bool ShouldRunSilentUpdateCheck(
        bool autoCheckEnabled,
        string? lastCheckDateKey,
        DateTime? nowOverride = null)
    {
        if (!autoCheckEnabled)
            return false;

        string todayKey = GetTodayDateKey(nowOverride);
        return !string.Equals(lastCheckDateKey?.Trim(), todayKey, StringComparison.Ordinal);
    }
}
