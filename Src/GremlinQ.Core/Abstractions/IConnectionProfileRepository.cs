using GremlinQ.Core.Models;

namespace GremlinQ.Core.Abstractions;

public interface IConnectionProfileRepository
{
    IReadOnlyList<ConnectionProfile> LoadAll();
    void Save(ConnectionProfile profile);
    void Delete(Guid id);
}