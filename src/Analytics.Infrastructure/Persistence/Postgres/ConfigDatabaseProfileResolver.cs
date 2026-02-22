using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Analytics.Infrastructure.Persistence.Postgres;

public sealed class ConfigDatabaseProfileResolver : IDatabaseProfileResolver
{
    private readonly DatabaseProfilesOptions _options;
    private readonly IConfiguration _configuration;

    public ConfigDatabaseProfileResolver(IOptions<DatabaseProfilesOptions> options, IConfiguration configuration)
    {
        _options = options.Value;
        _configuration = configuration;
    }

    public ResolvedDatabaseProfile Resolve(DatabaseProfileId profileId)
    {
        var profileKey = profileId.ToString();
        if (!_options.Profiles.TryGetValue(profileKey, out var profileConfig))
        {
            throw new InvalidOperationException($"Unknown database profile: '{profileKey}'. Only predefined profiles are allowed.");
        }

        if (!profileConfig.IsReadOnly)
        {
            throw new InvalidOperationException($"Database profile '{profileKey}' is not read-only and cannot be used.");
        }

        if (string.IsNullOrWhiteSpace(profileConfig.ConnectionStringName))
        {
            throw new InvalidOperationException($"Database profile '{profileKey}' has no connection string mapping.");
        }

        var connectionString = _configuration.GetConnectionString(profileConfig.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{profileConfig.ConnectionStringName}' for profile '{profileKey}' was not found.");
        }

        return new ResolvedDatabaseProfile(
            profileId,
            connectionString,
            profileConfig.CommandTimeoutSeconds,
            profileConfig.MaxRows,
            profileConfig.IsReadOnly);
    }
}
