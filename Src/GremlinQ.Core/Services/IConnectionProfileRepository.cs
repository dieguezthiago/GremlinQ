using GremlinQ.Core.Models;

namespace GremlinQ.Core.Services;

public interface IConnectionProfileRepository
{
    IReadOnlyList<ConnectionProfile> Load(string connectionsFolder);
}
