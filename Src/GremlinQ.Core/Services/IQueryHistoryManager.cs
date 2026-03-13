using GremlinQ.Core.Models;

namespace GremlinQ.Core.Services;

public interface IQueryHistoryManager
{
    IReadOnlyList<HistoryEntry> Items { get; }
    void Add(string query);
}
