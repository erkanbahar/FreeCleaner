using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace FreeCleaner.Services;

/// <summary>
/// Writes WPF data-binding errors to errors.log. A misspelled binding path does not throw in WPF: the field
/// just stays empty, so without this such mistakes go unnoticed. Each distinct message is logged once per
/// session; the UI smoke test fails on any of them.
/// </summary>
public sealed class BindingErrorListener : TraceListener
{
    private readonly ConcurrentDictionary<string, byte> _errors = new(StringComparer.Ordinal);

    public static BindingErrorListener? Instance { get; private set; }

    public IReadOnlyCollection<string> Errors => _errors.Keys.ToList();

    public static void Install()
    {
        if (Instance is not null)
            return;

        try
        {
            var listener = new BindingErrorListener();
            // Refresh() switches WPF tracing on even without a debugger attached.
            PresentationTraceSources.Refresh();
            TraceSource source = PresentationTraceSources.DataBindingSource;
            source.Listeners.Add(listener);
            source.Switch.Level = SourceLevels.Error;
            Instance = listener;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Security.SecurityException or NotSupportedException)
        {
            ErrorLogService.Log("Binding Listener", ex);
        }
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
        => Record(message);

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? format, params object?[]? args)
        => Record(args is { Length: > 0 } && format is not null ? SafeFormat(format, args) : format);

    public override void Write(string? message)
    {
        // Fragments arrive through WriteLine/TraceEvent as well; ignoring them avoids half messages.
    }

    public override void WriteLine(string? message) => Record(message);

    private void Record(string? message)
    {
        if (BindingErrorFilter.IsBenign(message))
            return;

        string text = message!.Trim();
        if (_errors.TryAdd(text, 0))
            ErrorLogService.Log("BINDING", text);
    }

    private static string SafeFormat(string format, object?[] args)
    {
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch (FormatException)
        {
            return format + " " + string.Join(" | ", args);
        }
    }
}
