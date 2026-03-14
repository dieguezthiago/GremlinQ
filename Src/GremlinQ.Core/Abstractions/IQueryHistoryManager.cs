using GremlinQ.Core.Models;

namespace GremlinQ.Core.Abstractions;

public interface IQueryHistoryManager
{
    IReadOnlyList<HistoryEntry> Items { get; }
    void Add(string query);
}