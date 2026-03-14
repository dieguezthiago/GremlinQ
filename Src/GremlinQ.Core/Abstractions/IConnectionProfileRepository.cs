using GremlinQ.Core.Models;

namespace GremlinQ.Core.Abstractions;

public interface IConnectionProfileRepository
{
    IReadOnlyList<ConnectionProfile> Load(string connectionsFolder);
}
