using FluentAssertions;
using NGB.Metadata.Schema;
using NGB.PostgreSql.Schema.Internal;
using NGB.PostgreSql.Tests.TestDoubles;
using Xunit;

namespace NGB.PostgreSql.Tests.Schema;

public sealed class PostgresSchemaValidationChecksFullCoverageTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task Constraint_fallback_queries_catalog_and_reports_only_a_missing_constraint(
        int matchCount,
        bool shouldReportError)
    {
        var connection = new RecordingDbConnection(scalar: _ => matchCount);
        var errors = new List<string>();

        await PostgresSchemaValidationChecks.RequireConstraintAsync(
            new RecordingUnitOfWork(connection),
            EmptySnapshot(),
            "ck_example",
            "example",
            errors,
            CancellationToken.None);

        errors.Should().HaveCount(shouldReportError ? 1 : 0);
        if (shouldReportError)
            errors.Should().ContainSingle("Missing constraint 'ck_example' on 'example'.");
        connection.Commands.Should().ContainSingle(command =>
            command.CommandText.Contains("FROM pg_constraint", StringComparison.Ordinal));
    }

    private static DbSchemaSnapshot EmptySnapshot()
        => new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<DbColumnSchema>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<DbForeignKeySchema>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<DbIndexSchema>>(StringComparer.OrdinalIgnoreCase));
}
