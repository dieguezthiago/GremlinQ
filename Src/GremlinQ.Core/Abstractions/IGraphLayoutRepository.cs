using GremlinQ.Core.Models;

namespace GremlinQ.Core.Abstractions;

public interface IGraphLayoutRepository
{
    IReadOnlyDictionary<string, NodePosition> Load(Guid profileId);
    void Save(Guid profileId, IReadOnlyDictionary<string, NodePosition> positions);
    void Delete(Guid profileId);
}
