using Analytics.Application.Models;

namespace Analytics.Application.Abstractions;

/// <summary>
/// Discovers database schema metadata dynamically by querying the database's information_schema.
/// </summary>
public interface ISchemaDiscoveryService
{
    /// <summary>
    /// Retrieves all table and column metadata for the given database profile.
    /// </summary>
    Task<SchemaMetadata> DiscoverAsync(DatabaseProfileId profileId, CancellationToken ct = default);
}
