using Analytics.Application.Models;
using Analytics.Infrastructure.Persistence.Postgres;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Analytics.UnitTests.Persistence;

public class ConfigDatabaseProfileResolverTests
{
    [Fact]
    public void Resolve_ValidReadOnlyProfile_ReturnsResolvedProfile()
    {
        var options = CreateOptions(new DatabaseProfileConfig
        {
            ConnectionStringName = "AnalyticsDb",
            CommandTimeoutSeconds = 30,
            MaxRows = 1000,
            IsReadOnly = true
        });
        var configuration = CreateConfiguration("AnalyticsDb", "Host=localhost;Database=analytics");
        var sut = new ConfigDatabaseProfileResolver(options, configuration);

        var result = sut.Resolve(DatabaseProfileId.AnalyticsReadOnly);

        result.ProfileId.Should().Be(DatabaseProfileId.AnalyticsReadOnly);
        result.ConnectionString.Should().Be("Host=localhost;Database=analytics");
        result.CommandTimeoutSeconds.Should().Be(30);
        result.MaxRows.Should().Be(1000);
        result.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Resolve_NonReadOnlyProfile_ThrowsInvalidOperationException()
    {
        var options = CreateOptions(new DatabaseProfileConfig
        {
            ConnectionStringName = "AnalyticsDb",
            IsReadOnly = false
        });
        var configuration = CreateConfiguration("AnalyticsDb", "Host=localhost;Database=test");
        var sut = new ConfigDatabaseProfileResolver(options, configuration);

        var act = () => sut.Resolve(DatabaseProfileId.AnalyticsReadOnly);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not read-only*");
    }

    [Fact]
    public void Resolve_UnknownProfile_ThrowsInvalidOperationException()
    {
        var options = Options.Create(new DatabaseProfilesOptions());
        var configuration = new ConfigurationBuilder().Build();
        var sut = new ConfigDatabaseProfileResolver(options, configuration);

        var act = () => sut.Resolve(DatabaseProfileId.AnalyticsReadOnly);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unknown database profile*");
    }

    [Fact]
    public void Resolve_MissingConnectionStringName_ThrowsInvalidOperationException()
    {
        var options = CreateOptions(new DatabaseProfileConfig
        {
            ConnectionStringName = "",
            IsReadOnly = true
        });
        var configuration = new ConfigurationBuilder().Build();
        var sut = new ConfigDatabaseProfileResolver(options, configuration);

        var act = () => sut.Resolve(DatabaseProfileId.AnalyticsReadOnly);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no connection string mapping*");
    }

    [Fact]
    public void Resolve_ConnectionStringNotFound_ThrowsInvalidOperationException()
    {
        var options = CreateOptions(new DatabaseProfileConfig
        {
            ConnectionStringName = "NonExistentDb",
            IsReadOnly = true
        });
        var configuration = new ConfigurationBuilder().Build();
        var sut = new ConfigDatabaseProfileResolver(options, configuration);

        var act = () => sut.Resolve(DatabaseProfileId.AnalyticsReadOnly);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*was not found*");
    }

    [Theory]
    [InlineData(15, 500)]
    [InlineData(30, 1000)]
    [InlineData(60, 5000)]
    public void Resolve_ReturnsConfiguredTimeoutAndMaxRows(int timeout, int maxRows)
    {
        var options = CreateOptions(new DatabaseProfileConfig
        {
            ConnectionStringName = "TestDb",
            CommandTimeoutSeconds = timeout,
            MaxRows = maxRows,
            IsReadOnly = true
        });
        var configuration = CreateConfiguration("TestDb", "Host=test");
        var sut = new ConfigDatabaseProfileResolver(options, configuration);

        var result = sut.Resolve(DatabaseProfileId.AnalyticsReadOnly);

        result.CommandTimeoutSeconds.Should().Be(timeout);
        result.MaxRows.Should().Be(maxRows);
    }

    private static IOptions<DatabaseProfilesOptions> CreateOptions(DatabaseProfileConfig config)
    {
        return Options.Create(new DatabaseProfilesOptions
        {
            Profiles = new Dictionary<string, DatabaseProfileConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["AnalyticsReadOnly"] = config
            }
        });
    }

    private static IConfiguration CreateConfiguration(string connectionStringName, string connectionString)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{connectionStringName}"] = connectionString
            })
            .Build();
    }
}
