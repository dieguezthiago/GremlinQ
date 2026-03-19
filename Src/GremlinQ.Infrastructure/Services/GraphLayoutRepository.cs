using System.Text.Json;
using GremlinQ.Core.Abstractions;
using GremlinQ.Core.Models;

namespace GremlinQ.Infrastructure.Services;

/// <summary>
///     Persists graph node positions to <c>%APPDATA%\GremlinQ\layouts\{profileId}.json</c>.
///     One file per connection profile; plain JSON (no encryption needed for coordinates).
/// </summary>
public sealed class GraphLayoutRepository : IGraphLayoutRepository
{
    private static readonly string LayoutsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GremlinQ",
        "layouts");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public IReadOnlyDictionary<string, NodePosition> Load(Guid profileId)
    {
        var path = FilePath(profileId);
        if (!File.Exists(path)) return new Dictionary<string, NodePosition>();

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, NodePosition>>(json, JsonOptions)
                   ?? new Dictionary<string, NodePosition>();
        }
        catch
        {
            return new Dictionary<string, NodePosition>();
        }
    }

    public void Save(Guid profileId, IReadOnlyDictionary<string, NodePosition> positions)
    {
        Directory.CreateDirectory(LayoutsDir);
        var path = FilePath(profileId);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(positions, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    public void Delete(Guid profileId)
    {
        var path = FilePath(profileId);
        if (File.Exists(path)) File.Delete(path);
    }

    private static string FilePath(Guid profileId) =>
        Path.Combine(LayoutsDir, $"{profileId}.json");
}
