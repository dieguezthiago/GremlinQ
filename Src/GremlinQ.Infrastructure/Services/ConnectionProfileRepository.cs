using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GremlinQ.Core.Abstractions;
using GremlinQ.Core.Models;

namespace GremlinQ.Infrastructure.Services;

/// <summary>
///     Persists <see cref="ConnectionProfile" /> instances to <c>%APPDATA%\GremlinQ\connections.json</c>.
///     The <c>Key</c> field is encrypted at rest with Windows DPAPI (current-user scope).
/// </summary>
public sealed class ConnectionProfileRepository : IConnectionProfileRepository
{
    private static readonly string StoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GremlinQ",
        "connections.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public IReadOnlyList<ConnectionProfile> LoadAll()
    {
        if (!File.Exists(StoragePath))
            return [];

        var json = File.ReadAllText(StoragePath);
        var stored = JsonSerializer.Deserialize<StoredConnectionProfile[]>(json, JsonOptions);
        return stored is null ? [] : [.. stored.Select(ToProfile)];
    }

    public void Save(ConnectionProfile profile)
    {
        var list = LoadStored();
        var idx = list.FindIndex(s => s.Id == profile.Id);
        var stored = ToStored(profile);

        if (idx >= 0)
            list[idx] = stored;
        else
            list.Add(stored);

        WriteStored(list);
    }

    public void Delete(Guid id)
    {
        var list = LoadStored();
        list.RemoveAll(s => s.Id == id);
        WriteStored(list);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private List<StoredConnectionProfile> LoadStored()
    {
        if (!File.Exists(StoragePath)) return [];
        var json = File.ReadAllText(StoragePath);
        return JsonSerializer.Deserialize<List<StoredConnectionProfile>>(json, JsonOptions) ?? [];
    }

    private static void WriteStored(List<StoredConnectionProfile> list)
    {
        var dir = Path.GetDirectoryName(StoragePath)!;
        Directory.CreateDirectory(dir);
        var tmp = StoragePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list, JsonOptions));
        File.Move(tmp, StoragePath, true);
    }

    private static ConnectionProfile ToProfile(StoredConnectionProfile s)
    {
        return new ConnectionProfile
        {
            Id = s.Id,
            Name = s.Name,
            Host = s.Host,
            Port = s.Port,
            EnableSsl = s.EnableSsl,
            Database = s.Database,
            Collection = s.Collection,
            Key = Decrypt(s.EncryptedKey)
        };
    }

    private static StoredConnectionProfile ToStored(ConnectionProfile p)
    {
        return new StoredConnectionProfile
        {
            Id = p.Id,
            Name = p.Name,
            Host = p.Host,
            Port = p.Port,
            EnableSsl = p.EnableSsl,
            Database = p.Database,
            Collection = p.Collection,
            EncryptedKey = Encrypt(p.Key)
        };
    }

    private static string Encrypt(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string Decrypt(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return string.Empty;
        var bytes = Convert.FromBase64String(base64);
        var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }
}

internal sealed class StoredConnectionProfile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public bool EnableSsl { get; set; }
    public string Database { get; set; } = string.Empty;
    public string Collection { get; set; } = string.Empty;
    public string EncryptedKey { get; set; } = string.Empty;
}