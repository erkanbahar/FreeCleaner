namespace FreeCleaner.Services;

/// <summary>
/// Lazily constructs and caches modular views on first navigation to improve startup speed and memory footprint.
/// </summary>
public sealed class LazyViewRegistry
{
    private readonly Dictionary<string, Func<object>> _factories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object> _cache = new(StringComparer.OrdinalIgnoreCase);

    public int RegisteredCount => _factories.Count;
    public int LoadedCount => _cache.Count;

    public void Register(string sectionKey, Func<object> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionKey);
        ArgumentNullException.ThrowIfNull(factory);
        _factories[sectionKey] = factory;
    }

    public bool HasView(string? sectionKey)
        => !string.IsNullOrWhiteSpace(sectionKey) && _factories.ContainsKey(sectionKey);

    public object? GetOrCreate(string? sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
            return null;

        if (_cache.TryGetValue(sectionKey, out object? existing))
            return existing;

        if (!_factories.TryGetValue(sectionKey, out Func<object>? factory))
            return null;

        object created = factory();
        _cache[sectionKey] = created;
        return created;
    }

    public void Clear() => _cache.Clear();
}
