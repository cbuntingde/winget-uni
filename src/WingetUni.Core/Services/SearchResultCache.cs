namespace WingetUni.Core.Services;

/// <summary>
/// Remembers recent searches so that repeating one does not run the managers again.
/// </summary>
/// <remarks>
/// A search costs a separate process per manager and can take seconds, while a user often
/// repeats the same query, pages back and forth, or returns to a term they typed a moment ago.
/// The cache is small and fixed in size, and the shell empties it whenever the installed set may
/// have changed, because a result records whether a package is already installed.
/// </remarks>
public sealed class SearchResultCache
{
    private readonly int _capacity;
    private readonly Dictionary<string, PackageQueryOutcome> _entries = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    public SearchResultCache(int capacity = 24)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    /// <summary>Builds the key for one search: the term, the limit, and the managers asked.</summary>
    public static string Key(string query, int limit, IEnumerable<string> managerIds)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(managerIds);

        // The managers are sorted so that ticking the same ones in a different order still hits
        // the same entry.
        var managers = managerIds.OrderBy(id => id, StringComparer.Ordinal);
        return $"{limit}\u001f{string.Join(',', managers)}\u001f{query}";
    }

    public bool TryGet(string key, out PackageQueryOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _entries.TryGetValue(key, out outcome!);
    }

    public void Set(string key, PackageQueryOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(outcome);

        if (_entries.ContainsKey(key))
        {
            _entries[key] = outcome;
            return;
        }

        while (_order.Count >= _capacity)
        {
            _entries.Remove(_order.Dequeue());
        }

        _entries[key] = outcome;
        _order.Enqueue(key);
    }

    public void Clear()
    {
        _entries.Clear();
        _order.Clear();
    }
}
