using System.IO;

namespace FreeCleaner.Services;

/// <summary>Command-line switches of the UI smoke test (see SmokeTestRunner).</summary>
/// <param name="Enabled"><c>--smoke-test</c>: open the window, visit every section in both languages, write a report, exit.</param>
/// <param name="ReportDirectory"><c>--smoke-report &lt;folder&gt;</c>: where smoke-report.txt (and screenshots) go.</param>
/// <param name="Screenshots"><c>--smoke-screenshots</c>: save a PNG of every section in both languages.</param>
public sealed record SmokeTestOptions(bool Enabled, string ReportDirectory, bool Screenshots)
{
    public static string DefaultReportDirectory => Path.Combine(Path.GetTempPath(), "FreeCleaner-smoke");

    public static SmokeTestOptions Disabled { get; } = new(false, DefaultReportDirectory, false);

    public static SmokeTestOptions Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
            return Disabled;

        bool enabled = false;
        bool screenshots = false;
        string dir = DefaultReportDirectory;
        for (int i = 0; i < args.Count; i++)
        {
            string a = (args[i] ?? string.Empty).Trim();
            if (a.Equals("--smoke-test", StringComparison.OrdinalIgnoreCase))
                enabled = true;
            else if (a.Equals("--smoke-screenshots", StringComparison.OrdinalIgnoreCase))
                screenshots = true;
            else if (a.Equals("--smoke-report", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                string value = (args[++i] ?? string.Empty).Trim().Trim('"');
                if (value.Length > 0)
                    dir = value;
            }
        }

        return enabled ? new SmokeTestOptions(true, dir, screenshots) : Disabled;
    }
}

/// <summary>Decides which WPF data-binding trace messages are real problems.</summary>
public static class BindingErrorFilter
{
    /// <summary>
    /// Well-known harmless messages produced by WPF's own templates rather than FreeCleaner's XAML:
    /// ComboBoxItem/MenuItem looking up content alignment from an ancestor, and bindings of rows that a
    /// virtualizing list has just recycled ("{DisconnectedItem}").
    /// </summary>
    public static bool IsBenign(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return true;

        if (message.Contains("{DisconnectedItem}", StringComparison.Ordinal) ||
            message.Contains("'NamedObject'", StringComparison.Ordinal))
            return true;

        return message.Contains("RelativeSource FindAncestor", StringComparison.Ordinal) &&
               (message.Contains("Path=HorizontalContentAlignment", StringComparison.Ordinal) ||
                message.Contains("Path=VerticalContentAlignment", StringComparison.Ordinal));
    }
}
