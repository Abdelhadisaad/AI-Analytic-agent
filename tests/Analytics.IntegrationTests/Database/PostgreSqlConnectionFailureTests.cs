using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Analytics.Infrastructure.Persistence.Postgres;
using Analytics.Infrastructure.Validation;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Analytics.IntegrationTests.Database;

public class PostgreSqlConnectionFailureTests
{
    #region Connection Failure Scenarios

    [Fact]
    public async Task ExecuteAsync_DatabaseUnreachable_ThrowsNpgsqlException()
    {
        // Arrange - Use non-existent host
        var executor = CreateExecutorWithConnectionString(
            "Host=non-existent-host;Database=test;Username=test;Password=test;Timeout=2");

        const string sql = "SELECT 1";

        // Act
        var act = () => executor.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NpgsqlException>();
    }

    [Fact]
    public async Task ExecuteAsync_InvalidCredentials_ThrowsNpgsqlException()
    {
        // Arrange - Use localhost with wrong credentials
        var executor = CreateExecutorWithConnectionString(
            "Host=localhost;Port=5432;Database=analytics;Username=wrong;Password=wrong;Timeout=2");

        const string sql = "SELECT 1";

        // Act
        var act = () => executor.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NpgsqlException>();
    }

    [Fact]
    public async Task ExecuteAsync_WrongPort_ThrowsNpgsqlException()
    {
        // Arrange - Use wrong port
        var executor = CreateExecutorWithConnectionString(
            "Host=localhost;Port=9999;Database=analytics;Username=test;Password=test;Timeout=2");

        const string sql = "SELECT 1";

        // Act
        var act = () => executor.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NpgsqlException>();
    }

    [Fact]
    public async Task ExecuteAsync_NonExistentDatabase_ThrowsNpgsqlException()
    {
        // Arrange - Use non-existent database (requires valid host)
        var executor = CreateExecutorWithConnectionString(
            "Host=localhost;Port=5432;Database=non_existent_db_12345;Username=test;Password=test;Timeout=2");

        const string sql = "SELECT 1";

        // Act
        var act = () => executor.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NpgsqlException>();
    }

    #endregion

    #region Configuration Error Scenarios

    [Fact]
    public void Resolve_MissingConnectionString_ThrowsInvalidOperationException()
    {
        // Arrange
        var profileOptions = Options.Create(new DatabaseProfilesOptions
        {
            Profiles = new Dictionary<string, DatabaseProfileConfig>
            {
                ["AnalyticsReadOnly"] = new()
                {
                    ConnectionStringName = "NonExistentConnectionString",
                    IsReadOnly = true,
                    CommandTimeoutSeconds = 30,
                    MaxRows = 1000
                }
            }
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>()) // Empty config
            .Build();

        var resolver = new ConfigDatabaseProfileResolver(profileOptions, configuration);

        // Act
        var act = () => resolver.Resolve(DatabaseProfileId.AnalyticsReadOnly);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Connection string*not found*");
    }

    [Fact]
    public void Resolve_UnknownProfile_ThrowsInvalidOperationException()
    {
        // Arrange - Create resolver with missing profile
        var profileOptions = Options.Create(new DatabaseProfilesOptions
        {
            Profiles = new Dictionary<string, DatabaseProfileConfig>() // No profiles defined
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var resolver = new ConfigDatabaseProfileResolver(profileOptions, configuration);

        // Act
        var act = () => resolver.Resolve(DatabaseProfileId.AnalyticsReadOnly);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown database profile*");
    }

    [Fact]
    public void Resolve_NonReadOnlyProfile_ThrowsInvalidOperationException()
    {
        // Arrange
        var profileOptions = Options.Create(new DatabaseProfilesOptions
        {
            Profiles = new Dictionary<string, DatabaseProfileConfig>
            {
                ["AnalyticsReadOnly"] = new()
                {
                    ConnectionStringName = "AnalyticsDb",
                    IsReadOnly = false, // Not read-only!
                    CommandTimeoutSeconds = 30,
                    MaxRows = 1000
                }
            }
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AnalyticsDb"] = "Host=localhost;Database=test"
            })
            .Build();

        var resolver = new ConfigDatabaseProfileResolver(profileOptions, configuration);

        // Act
        var act = () => resolver.Resolve(DatabaseProfileId.AnalyticsReadOnly);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not read-only*");
    }

    #endregion

    #region Helpers

    private static IReadOnlyQueryExecutor CreateExecutorWithConnectionString(string connectionString)
    {
        var profileOptions = Options.Create(new DatabaseProfilesOptions
        {
            Profiles = new Dictionary<string, DatabaseProfileConfig>
            {
                ["AnalyticsReadOnly"] = new()
                {
                    ConnectionStringName = "AnalyticsDb",
                    IsReadOnly = true,
                    CommandTimeoutSeconds = 2,
                    MaxRows = 1000
                }
            }
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AnalyticsDb"] = connectionString
            })
            .Build();

        var profileResolver = new ConfigDatabaseProfileResolver(profileOptions, configuration);
        var sqlValidatorOptions = Options.Create(new SqlValidationOptions());
        var sqlValidator = new RegexSqlValidator(sqlValidatorOptions);

        return new NpgsqlReadOnlyQueryExecutor(profileResolver, sqlValidator);
    }

    #endregion
}
