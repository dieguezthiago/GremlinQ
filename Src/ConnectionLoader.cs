using System.IO;
using System.Text.Json;

namespace Gremlinq;

/// <summary>Loads and sorts <see cref="ConnectionProfile"/> instances from the <c>connections</c> folder.</summary>
public static class ConnectionLoader
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Scans the <paramref name="connectionsFolder"/>, deserializes every <c>*.json</c> file into a
    /// <see cref="ConnectionProfile"/>, and returns them sorted: Emulator → Dev → Staging → Prod → others (alphabetically).
    /// </summary>
    /// <param name="connectionsFolder">Path to the folder containing environment JSON files.</param>
    /// <returns>Ordered list of connection profiles.</returns>
    public static List<ConnectionProfile> Load(string connectionsFolder)
    {
        ArgumentNullException.ThrowIfNull(connectionsFolder);

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

    private static int SortKey(ConnectionProfile p) => p.Name.ToLowerInvariant() switch
    {
        var n when n.Contains("emulator") => 0,
        var n when n.Contains("dev")      => 1,
        var n when n.Contains("staging")  => 2,
        var n when n.Contains("prod")     => 3,
        _                                 => 4
    };
}
