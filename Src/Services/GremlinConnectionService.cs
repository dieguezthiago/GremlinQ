using Gremlin.Net.Driver;
using Gremlinq.Services.Interfaces;

namespace Gremlinq.Services;

/// <summary>Manages the lifecycle of a <see cref="GremlinClient" /> connection.</summary>
public sealed class GremlinConnectionService : IGremlinConnectionService, IDisposable
{
    private GremlinClient? _client;

    public void Dispose()
    {
        Disconnect();
    }

    public bool IsConnected => _client is not null;
    public ConnectionProfile? ActiveProfile { get; private set; }

    public void Connect(ConnectionProfile profile, string key)
    {
        Disconnect();

        var server = new GremlinServer(
            profile.Host,
            profile.Port,
            profile.EnableSsl,
            $"/dbs/{profile.Database}/colls/{profile.Collection}",
            key);

        var poolSettings = new ConnectionPoolSettings
        {
            MaxInProcessPerConnection = 32,
            PoolSize = 4
        };

        _client = new GremlinClient(server, new CosmosDbMessageSerializer(), poolSettings);
        ActiveProfile = profile;
    }

    public void Disconnect()
    {
        _client?.Dispose();
        _client = null;
        ActiveProfile = null;
    }

    public async Task<IEnumerable<dynamic>> SubmitAsync(string query)
    {
        if (_client is null)
            throw new InvalidOperationException("Not connected.");

        return await _client.SubmitAsync<dynamic>(query);
    }
}