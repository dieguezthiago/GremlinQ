namespace GremlinQ.Services.Interfaces;

public interface IGremlinConnectionService
{
    bool IsConnected { get; }
    ConnectionProfile? ActiveProfile { get; }

    void Connect(ConnectionProfile profile, string key);
    void Disconnect();

    /// <summary>Submits a raw Gremlin query and returns the result items.</summary>
    Task<IEnumerable<dynamic>> SubmitAsync(string query);
}