using Gremlinq.Models;

namespace Gremlinq.Services.Interfaces;

public interface IQueryHistoryManager
{
    IReadOnlyList<HistoryEntry> Items { get; }
    void Add(string query);
}