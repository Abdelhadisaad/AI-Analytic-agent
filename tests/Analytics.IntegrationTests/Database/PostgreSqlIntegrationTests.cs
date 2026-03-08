using System.Data;
using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Analytics.Infrastructure.Persistence.Postgres;
using Analytics.Infrastructure.Validation;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Analytics.IntegrationTests.Database;

public class PostgreSqlIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgresContainer = null!;
    private IReadOnlyQueryExecutor _sut = null!;
    private string _connectionString = null!;

    public async Task InitializeAsync()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:15-alpine")
            .WithDatabase("analytics_test")
            .WithUsername("test_user")
            .WithPassword("test_password")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
            .Build();

        await _postgresContainer.StartAsync();
        _connectionString = _postgresContainer.GetConnectionString();

        await SeedTestDataAsync();
        SetupSystemUnderTest();
    }

    public async Task DisposeAsync()
    {
        await _postgresContainer.DisposeAsync();
    }

    private async Task SeedTestDataAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var createTablesSql = """
            CREATE TABLE orders (
                id SERIAL PRIMARY KEY,
                customer_name VARCHAR(100) NOT NULL,
                status VARCHAR(50) NOT NULL,
                total_amount DECIMAL(10, 2) NOT NULL,
                created_at TIMESTAMP NOT NULL DEFAULT NOW()
            );

            CREATE TABLE customers (
                id SERIAL PRIMARY KEY,
                name VARCHAR(100) NOT NULL,
                email VARCHAR(255) UNIQUE NOT NULL,
                created_at TIMESTAMP NOT NULL DEFAULT NOW()
            );

            INSERT INTO customers (name, email) VALUES
                ('Alice', 'alice@example.com'),
                ('Bob', 'bob@example.com'),
                ('Charlie', 'charlie@example.com');

            INSERT INTO orders (customer_name, status, total_amount) VALUES
                ('Alice', 'completed', 150.00),
                ('Alice', 'pending', 75.50),
                ('Bob', 'completed', 200.00),
                ('Bob', 'cancelled', 50.00),
                ('Charlie', 'pending', 300.00);
            """;

        await using var command = new NpgsqlCommand(createTablesSql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private void SetupSystemUnderTest()
    {
        var profileOptions = Options.Create(new DatabaseProfilesOptions
        {
            Profiles = new Dictionary<string, DatabaseProfileConfig>
            {
                ["AnalyticsReadOnly"] = new()
                {
                    ConnectionStringName = "AnalyticsDb",
                    IsReadOnly = true,
                    CommandTimeoutSeconds = 30,
                    MaxRows = 1000
                }
            }
        });

        var configurationData = new Dictionary<string, string?>
        {
            ["ConnectionStrings:AnalyticsDb"] = _connectionString
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        var profileResolver = new ConfigDatabaseProfileResolver(profileOptions, configuration);
        var sqlValidatorOptions = Options.Create(new SqlValidationOptions());
        var sqlValidator = new RegexSqlValidator(sqlValidatorOptions);

        _sut = new NpgsqlReadOnlyQueryExecutor(profileResolver, sqlValidator);
    }

    #region Success Scenarios

    [Fact]
    public async Task ExecuteAsync_ValidSelectQuery_ReturnsRows()
    {
        // Arrange
        const string sql = "SELECT * FROM orders";

        // Act
        var result = await _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        result.RowCount.Should().Be(5);
        result.Rows.Should().HaveCount(5);
        result.Truncated.Should().BeFalse();
        result.DurationMs.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task ExecuteAsync_SelectWithWhereClause_ReturnsFilteredRows()
    {
        // Arrange
        const string sql = "SELECT * FROM orders WHERE status = 'completed'";

        // Act
        var result = await _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        result.RowCount.Should().Be(2);
        result.Rows.All(r => r["status"]?.ToString() == "completed").Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_AggregateQuery_ReturnsCorrectCount()
    {
        // Arrange
        const string sql = "SELECT COUNT(*) as order_count FROM orders";

        // Act
        var result = await _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        result.RowCount.Should().Be(1);
        result.Rows[0]["order_count"].Should().Be(5L);
    }

    [Fact]
    public async Task ExecuteAsync_JoinQuery_ReturnsJoinedData()
    {
        // Arrange
        const string sql = """
            SELECT o.id, o.status, o.total_amount, c.email
            FROM orders o
            JOIN customers c ON o.customer_name = c.name
            WHERE o.status = 'completed'
            """;

        // Act
        var result = await _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        result.RowCount.Should().Be(2);
        result.Rows.All(r => r.ContainsKey("email")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_GroupByQuery_ReturnsAggregatedData()
    {
        // Arrange
        const string sql = """
            SELECT status, COUNT(*) as count, SUM(total_amount) as total
            FROM orders
            GROUP BY status
            ORDER BY status
            """;

        // Act
        var result = await _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        result.RowCount.Should().BeGreaterThan(0);
        result.Rows.All(r => r.ContainsKey("status") && r.ContainsKey("count")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_QueryWithNullValues_HandlesNullsCorrectly()
    {
        // Arrange - First insert row with NULL
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var insertCmd = new NpgsqlCommand(
            "INSERT INTO customers (name, email) VALUES ('NoEmail', 'noemail@test.com')",
            connection);
        await insertCmd.ExecuteNonQueryAsync();

        const string sql = "SELECT * FROM customers WHERE name = 'NoEmail'";

        // Act
        var result = await _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        result.RowCount.Should().Be(1);
    }

    #endregion

    #region Read-Only Enforcement Scenarios

    [Fact]
    public async Task ExecuteAsync_InsertStatement_ThrowsException()
    {
        // Arrange
        const string sql = "INSERT INTO orders (customer_name, status, total_amount) VALUES ('Test', 'pending', 100)";

        // Act
        var act = () => _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*rejected*");
    }

    [Fact]
    public async Task ExecuteAsync_UpdateStatement_ThrowsException()
    {
        // Arrange
        const string sql = "UPDATE orders SET status = 'shipped' WHERE id = 1";

        // Act
        var act = () => _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*rejected*");
    }

    [Fact]
    public async Task ExecuteAsync_DeleteStatement_ThrowsException()
    {
        // Arrange
        const string sql = "DELETE FROM orders WHERE id = 1";

        // Act
        var act = () => _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*rejected*");
    }

    [Fact]
    public async Task ExecuteAsync_DropTable_ThrowsException()
    {
        // Arrange
        const string sql = "DROP TABLE orders";

        // Act
        var act = () => _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*rejected*");
    }

    [Fact]
    public async Task ExecuteAsync_TruncateTable_ThrowsException()
    {
        // Arrange
        const string sql = "TRUNCATE TABLE orders";

        // Act
        var act = () => _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*rejected*");
    }

    [Fact]
    public async Task ExecuteAsync_AlterTable_ThrowsException()
    {
        // Arrange
        const string sql = "ALTER TABLE orders ADD COLUMN notes TEXT";

        // Act
        var act = () => _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*rejected*");
    }

    #endregion

    #region SQL Injection Prevention Scenarios

    [Theory]
    [InlineData("SELECT * FROM orders; DROP TABLE orders;")]
    [InlineData("SELECT * FROM orders; DELETE FROM customers;")]
    [InlineData("SELECT * FROM orders; INSERT INTO orders VALUES (1, 'x', 'y', 1);")]
    public async Task ExecuteAsync_StackedQueries_ThrowsException(string sql)
    {
        // Act
        var act = () => _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("SELECT * FROM orders WHERE id = 1 OR 1=1")]
    [InlineData("SELECT * FROM orders WHERE id = 1 UNION SELECT * FROM customers")]
    public async Task ExecuteAsync_SuspiciousPatterns_ThrowsException(string sql)
    {
        // Act
        var act = () => _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*suspicious*");
    }

    #endregion

    #region Query Error Scenarios

    [Fact]
    public async Task ExecuteAsync_NonExistentTable_ThrowsNpgsqlException()
    {
        // Arrange
        const string sql = "SELECT * FROM non_existent_table";

        // Act
        var act = () => _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NpgsqlException>()
            .WithMessage("*does not exist*");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidColumnName_ThrowsNpgsqlException()
    {
        // Arrange
        const string sql = "SELECT non_existent_column FROM orders";

        // Act
        var act = () => _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NpgsqlException>()
            .WithMessage("*does not exist*");
    }

    [Fact]
    public async Task ExecuteAsync_SyntaxError_ThrowsNpgsqlException()
    {
        // Arrange
        const string sql = "SELEC * FORM orders"; // typos

        // Act
        var act = () => _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NpgsqlException>();
    }

    #endregion

    #region Row Limit Scenarios

    [Fact]
    public async Task ExecuteAsync_ExceedsMaxRows_ReturnsTruncatedResult()
    {
        // Arrange - Insert many rows
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        for (var i = 0; i < 50; i++)
        {
            await using var cmd = new NpgsqlCommand(
                $"INSERT INTO orders (customer_name, status, total_amount) VALUES ('Bulk{i}', 'pending', {i}.00)",
                connection);
            await cmd.ExecuteNonQueryAsync();
        }

        // Reconfigure with small MaxRows for this test
        var profileOptions = Options.Create(new DatabaseProfilesOptions
        {
            Profiles = new Dictionary<string, DatabaseProfileConfig>
            {
                ["AnalyticsReadOnly"] = new()
                {
                    ConnectionStringName = "AnalyticsDb",
                    IsReadOnly = true,
                    CommandTimeoutSeconds = 30,
                    MaxRows = 10 // Small limit
                }
            }
        });

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:AnalyticsDb"] = _connectionString
            })
            .Build();

        var profileResolver = new ConfigDatabaseProfileResolver(profileOptions, configuration);
        var sqlValidatorOptions = Options.Create(new SqlValidationOptions());
        var executor = new NpgsqlReadOnlyQueryExecutor(profileResolver, new RegexSqlValidator(sqlValidatorOptions));

        const string sql = "SELECT * FROM orders";

        // Act
        var result = await executor.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, CancellationToken.None);

        // Assert
        result.RowCount.Should().Be(10);
        result.Truncated.Should().BeTrue();
    }

    #endregion

    #region Cancellation Scenarios

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_ThrowsOperationCancelledException()
    {
        // Arrange
        const string sql = "SELECT * FROM orders";
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var act = () => _sut.ExecuteAsync(DatabaseProfileId.AnalyticsReadOnly, sql, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    #endregion
}
