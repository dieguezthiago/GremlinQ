namespace GremlinQ.Services.Interfaces;

public interface IConnectionProfileRepository
{
    IReadOnlyList<ConnectionProfile> Load(string connectionsFolder);
}