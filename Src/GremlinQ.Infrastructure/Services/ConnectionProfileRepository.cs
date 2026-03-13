using System.IO;
using System.Text.Json;
using GremlinQ.Core.Models;
using GremlinQ.Core.Services;

namespace GremlinQ.Infrastructure.Services;

/// <summary>Loads and sorts <see cref="ConnectionProfile" /> instances from the connections folder.</summary>
public sealed class ConnectionProfileRepository : IConnectionProfileRepository
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyList<ConnectionProfile> Load(string connectionsFolder)
    {
        if (!Directory.Exists(connectionsFolder))
            return [];

        var profiles = new List<ConnectionProfile>();

        foreach (var file in Directory.EnumerateFiles(connectionsFolder, "*.json"))
        {
            var json = File.ReadAllText(file);
            var profile = JsonSerializer.Deserialize<ConnectionProfile>(json, _jsonOptions);
            if (profile is not null)
                profiles.Add(profile);
        }

        return [.. profiles.OrderBy(SortKey)];
    }

    private static int SortKey(ConnectionProfile p)
    {
        return p.Name.ToLowerInvariant() switch
        {
            var n when n.Contains("emulator") => 0,
            var n when n.Contains("dev") => 1,
            var n when n.Contains("staging") => 2,
            var n when n.Contains("prod") => 3,
            _ => 4
        };
    }
}
