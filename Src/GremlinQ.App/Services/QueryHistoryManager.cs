using GremlinQ.Core.Abstractions;
using GremlinQ.Core.Models;

namespace GremlinQ.App.Services;

/// <summary>Maintains a bounded, deduplicated LIFO history of executed queries.</summary>
public sealed class QueryHistoryManager : IQueryHistoryManager
{
    private const int MaxItems = 50;
    private readonly List<HistoryEntry> _items = [];

    public IReadOnlyList<HistoryEntry> Items => _items;

    public void Add(string query)
    {
        _items.RemoveAll(h => h.Query == query);
        _items.Insert(0, new HistoryEntry(query));

        if (_items.Count > MaxItems)
            _items.RemoveAt(_items.Count - 1);
    }
}