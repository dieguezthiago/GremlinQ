using GremlinQ.Models;

namespace GremlinQ.Services.Interfaces;

public interface IQueryHistoryManager
{
    IReadOnlyList<HistoryEntry> Items { get; }
    void Add(string query);
}