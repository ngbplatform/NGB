using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using NGB.Persistence.Migrations;
using NGB.PostgreSql.Bootstrap;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P0: Schema bootstrapper must be the single source of truth and must not silently miss migrations.
///
/// This test is a contract guard:
/// - If a new IDdlObject is added under NGB.PostgreSql.Migrations.* and not wired into DatabaseBootstrapper,
///   this must fail immediately.
/// </summary>
public sealed class DatabaseBootstrapper_MigrationSetCompleteness_P0Tests
{
    [Fact]
    public void Bootstrapper_MustInclude_Every_IDdlObject_Implementation_InAssembly()
    {
        // Arrange
        var assembly = typeof(DatabaseBootstrapper).Assembly;

        var allDdlTypes = assembly
            .GetTypes()
            .Where(t => typeof(IDdlObject).IsAssignableFrom(t))
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("NGB.PostgreSql.Migrations", StringComparison.Ordinal))
            .Where(t => t.Name.EndsWith("Migration", StringComparison.Ordinal))
            .ToList();

        var wired = BuildBootstrapperSet();
        var wiredTypes = wired.Select(x => x.GetType()).ToList();

        // Assert
        wiredTypes.Should().OnlyHaveUniqueItems("bootstrapper must not contain duplicates");
        wiredTypes.Should().BeEquivalentTo(allDdlTypes, options => options.WithoutStrictOrdering());
    }

    [Fact]
    public void Bootstrapper_MustBeDeterministicallyOrdered_ReferencedTablesMustBeCreatedBeforeIndexesAndTriggers()
    {
        // Arrange
        var wired = BuildBootstrapperSet().ToList();
        var createdTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < wired.Count; i++)
        {
            var ddl = wired[i];
            var sql = ddl.Generate();

            // Allow a DDL object to both create and use a table within the same SQL blob.
            // We enforce cross-object ordering, which is the common source of regressions.
            foreach (var raw in ExtractCreatedTables(sql))
                if (TryNormalizeLiteralTableName(raw, out var t))
                    createdTables.Add(t);

            foreach (var raw in ExtractIndexedTables(sql))
                if (TryNormalizeLiteralTableName(raw, out var t))
                    createdTables.Should().Contain(
                        t,
                        $"index must be created after its table (item #{i}: {ddl.GetType().Name})");

            foreach (var raw in ExtractTriggeredTables(sql))
                if (TryNormalizeLiteralTableName(raw, out var t))
                    createdTables.Should().Contain(
                        t,
                        $"trigger must be created after its table (item #{i}: {ddl.GetType().Name})");

            foreach (var raw in ExtractReferencedTables(sql))
                if (TryNormalizeLiteralTableName(raw, out var t))
                    createdTables.Should().Contain(
                        t,
                        $"referenced table must be created before being referenced (item #{i}: {ddl.GetType().Name})");
        }

        // A second safety net: the list itself must be deterministic.
        wired.Select(x => x.GetType().FullName).Should().NotContainNulls();
    }

    private static IDdlObject[] BuildBootstrapperSet()
    {
        var mi = typeof(DatabaseBootstrapper).GetMethod(
            "BuildPlatformDdlObjects",
            BindingFlags.Static | BindingFlags.NonPublic);

        mi.Should().NotBeNull("DatabaseBootstrapper must expose the built migration set for contract tests");

        var result = mi!.Invoke(null, null);
        result.Should().BeOfType<IDdlObject[]>();

        return (IDdlObject[])result!;
    }

    private static readonly Regex CreateTableRegex = new(
        @"\bCREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<name>[^\s(]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex CreateIndexOnRegex = new(
        @"\bCREATE\s+(?:UNIQUE\s+)?INDEX\b.*?\bON\s+(?:ONLY\s+)?(?<name>[^\s(]+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex CreateTriggerOnRegex = new(
        @"\bCREATE\s+TRIGGER\b.*?\bON\s+(?<name>[^\s(]+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static readonly Regex ReferencesRegex = new(
        @"\bREFERENCES\s+(?<name>[^\s(]+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // Matches literal identifiers like:
    //   table
    //   schema.table
    //   "table"
    //   "schema"."table"
    // Rejects dynamic SQL placeholders like %I or function tokens like quote_ident.
    private static readonly Regex LiteralTableRefRegex = new(
        "^(?:\\\"[^\\\"]+\\\"|[a-z_][a-z0-9_]*)(?:\\.(?:\\\"[^\\\"]+\\\"|[a-z_][a-z0-9_]*))*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IEnumerable<string> ExtractCreatedTables(string sql)
        => CreateTableRegex.Matches(sql).Select(m => m.Groups["name"].Value);

    private static IEnumerable<string> ExtractIndexedTables(string sql)
        => CreateIndexOnRegex.Matches(sql).Select(m => m.Groups["name"].Value);

    private static IEnumerable<string> ExtractTriggeredTables(string sql)
        => CreateTriggerOnRegex.Matches(sql).Select(m => m.Groups["name"].Value);

    private static IEnumerable<string> ExtractReferencedTables(string sql)
        => ReferencesRegex.Matches(sql).Select(m => m.Groups["name"].Value);

    private static bool TryNormalizeLiteralTableName(string raw, out string normalized)
    {
        normalized = "";

        var s = raw.Trim().TrimEnd(';');
        if (s.Length == 0)
            return false;

        // Dynamic SQL in migrations uses placeholders like %I (format()) or concatenations.
        // We can't prove ordering for those by static parsing, so we intentionally ignore them.
        if (s.Contains('%') || s.Contains('$') || s.Contains("||", StringComparison.Ordinal) || s.Contains('{') || s.Contains('}'))
            return false;

        if (!LiteralTableRefRegex.IsMatch(s))
            return false;

        normalized = NormalizeTableName(s);
        return normalized.Length > 0;
    }

    private static string NormalizeTableName(string raw)
    {
        var s = raw.Trim().TrimEnd(';');

        // handle schema-qualified names like public.table or "public"."table"
        var parts = s.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var last = parts.Length == 0 ? s : parts[^1];

        return last.Trim('"');
    }
}
