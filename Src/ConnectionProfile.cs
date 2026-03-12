namespace Gremlinq;

/// <summary>Represents the connection settings for a single Azure Cosmos DB / Gremlin environment.</summary>
public class ConnectionProfile
{
    /// <summary>Display name of the environment (e.g. "Development").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gremlin endpoint hostname.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gremlin endpoint port.</summary>
    public int Port { get; set; }

    /// <summary>Whether TLS/SSL is required for this endpoint.</summary>
    public bool EnableSsl { get; set; }

    /// <summary>Cosmos DB database name.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>Cosmos DB graph collection name.</summary>
    public string Collection { get; set; } = string.Empty;

    /// <summary>Primary key / password for authentication.</summary>
    public string Key { get; set; } = string.Empty;
}