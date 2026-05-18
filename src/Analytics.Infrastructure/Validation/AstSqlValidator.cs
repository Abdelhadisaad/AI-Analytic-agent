using Analytics.Application.Abstractions;
using Analytics.Application.Models;
using Microsoft.Extensions.Logging;
using SqlParser;
using SqlParser.Ast;
using SqlParser.Dialects;

namespace Analytics.Infrastructure.Validation;

/// <summary>
/// AST-based SQL validator that uses a real SQL parser (SqlParser-cs) to
/// structurally analyse generated SQL before execution.
///
/// <para><b>Why AST validation?</b></para>
/// <para>
/// The previous regex-only approach (see <see cref="RegexSqlValidator"/>)
/// has well-documented limitations:
/// <list type="bullet">
///   <item>False positives — blocked keywords inside string literals
///     (e.g. <c>WHERE action = 'DELETE'</c>) are incorrectly rejected.</item>
///   <item>Evasion risk — creative encoding, comment insertion (e.g.
///     <c>DR/**/OP</c>), or case mixing can bypass pattern matching.</item>
///   <item>No structural understanding — regex cannot verify that the
///     query is truly a single SELECT statement at the syntax tree level.</item>
/// </list>
/// </para>
///
/// <para><b>How AST validation works</b></para>
/// <para>
/// This validator parses the SQL string into an Abstract Syntax Tree using
/// the PostgreSQL dialect, then walks the tree to enforce structural rules:
/// <list type="number">
///   <item>The SQL must be syntactically valid PostgreSQL.</item>
///   <item>Exactly one statement is allowed (no statement chaining).</item>
///   <item>The statement must be a <c>SELECT</c> (any other statement
///     type — INSERT, UPDATE, DELETE, DROP, etc. — is rejected).</item>
/// </list>
/// </para>
///
/// <para><b>Defense-in-depth</b></para>
/// <para>
/// This validator is designed to work alongside the regex-based suspicious
/// pattern detection (via <see cref="CompositeSqlValidator"/>). The AST
/// handles structural correctness while regex adds heuristic warnings for
/// patterns like SQL comments, UNION injection, and system catalog access.
/// </para>
///
/// <para><b>References</b></para>
/// <list type="bullet">
///   <item>OWASP (2021) — Query Parameterization Cheat Sheet, robust validation principles</item>
///   <item>SqlParser-cs — .NET port of sqlparser-rs with PostgreSQL dialect support</item>
/// </list>
/// </summary>
public sealed class AstSqlValidator : ISqlValidator
{
    private readonly ILogger<AstSqlValidator> _logger;

    /// <summary>
    /// Statement types that are allowed through validation.
    /// Only SELECT queries are permitted in this read-only analytics context.
    /// </summary>
    private static readonly HashSet<Type> AllowedStatementTypes = new()
    {
        typeof(Statement.Select)
    };

    /// <summary>
    /// Statement types that are explicitly dangerous and should be blocked
    /// with a clear error message. Any type not in <see cref="AllowedStatementTypes"/>
    /// is also blocked, but these get specific error labels.
    /// </summary>
    private static readonly Dictionary<Type, string> BlockedStatementLabels = new()
    {
        { typeof(Statement.Insert), "INSERT" },
        { typeof(Statement.Update), "UPDATE" },
        { typeof(Statement.Delete), "DELETE" },
        { typeof(Statement.Drop), "DROP" },
        { typeof(Statement.AlterTable), "ALTER TABLE" },
        { typeof(Statement.Truncate), "TRUNCATE" },
        { typeof(Statement.CreateTable), "CREATE TABLE" },
        { typeof(Statement.CreateView), "CREATE VIEW" },
        { typeof(Statement.CreateIndex), "CREATE INDEX" },
        { typeof(Statement.Grant), "GRANT" },
        { typeof(Statement.Revoke), "REVOKE" },
        { typeof(Statement.Copy), "COPY" },
    };

    public AstSqlValidator(ILogger<AstSqlValidator> logger)
    {
        _logger = logger;
    }

    public SqlValidationResult Validate(string sql)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(sql))
        {
            errors.Add("SQL query is empty.");
            return SqlValidationResult.Invalid(errors);
        }

        // ── Step 1: Parse SQL into AST ──────────────────────────
        Sequence<Statement> statements;
        try
        {
            statements = new Parser().ParseSql(sql.Trim(), new PostgreSqlDialect());
        }
        catch (ParserException ex)
        {
            _logger.LogWarning("AST parse failed: {Error}", ex.Message);
            errors.Add($"SQL syntax error: {ex.Message}");
            return SqlValidationResult.Invalid(errors);
        }
        catch (TokenizeException ex)
        {
            _logger.LogWarning("AST tokenize failed: {Error}", ex.Message);
            errors.Add($"SQL tokenization error: {ex.Message}");
            return SqlValidationResult.Invalid(errors);
        }

        // ── Step 2: Exactly one statement ───────────────────────
        if (statements.Count == 0)
        {
            errors.Add("No SQL statements found.");
            return SqlValidationResult.Invalid(errors);
        }

        if (statements.Count > 1)
        {
            var types = string.Join(", ", statements.Select(s => GetStatementLabel(s)));
            errors.Add($"Multiple SQL statements detected ({statements.Count}): {types}. Only a single SELECT is allowed.");
            return SqlValidationResult.Invalid(errors);
        }

        // ── Step 3: Must be a SELECT statement ──────────────────
        var statement = statements[0];

        if (!AllowedStatementTypes.Contains(statement.GetType()))
        {
            var label = GetStatementLabel(statement);
            errors.Add($"Statement type '{label}' is not allowed. Only SELECT queries are permitted.");
            return SqlValidationResult.Invalid(errors);
        }

        _logger.LogDebug("AST validation passed: single SELECT statement");
        return SqlValidationResult.Valid();
    }

    private static string GetStatementLabel(Statement statement)
    {
        var type = statement.GetType();
        return BlockedStatementLabels.TryGetValue(type, out var label) ? label : type.Name;
    }
}
