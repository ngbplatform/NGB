using System.Data;
using FluentAssertions;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using Xunit;

namespace NGB.PostgreSql.Tests.OperationalRegisters;

public sealed class PostgresOperationalRegisterResourceRepositoryFullCoverageTests
{
    private static readonly Guid RegisterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime NowUtc = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Replace_rejects_missing_register_and_temp_ordinal_overflow()
    {
        var missing = Fixture(hasMovements: null).Repository;
        Func<Task> missingRegister = () => missing.ReplaceAsync(RegisterId, [], NowUtc);
        await missingRegister.Should().ThrowAsync<OperationalRegisterNotFoundException>();

        var existing = new[] { Resource("amount", "amount", "amount", int.MaxValue) };
        var overflow = Fixture(hasMovements: true, existing: existing).Repository;
        Func<Task> ordinalOverflow = () => overflow.ReplaceAsync(
            RegisterId,
            [Definition("amount", 1)],
            NowUtc);
        var error = await ordinalOverflow.Should().ThrowAsync<OperationalRegisterResourcesAppendOnlyViolationException>();
        error.Which.Reason.Should().Be("ordinal_overflow");
    }

    [Fact]
    public void Immutability_allows_mutable_or_unchanged_resources_and_reports_sorted_removals_and_renames()
    {
        var existing = new[]
        {
            Resource("A_B", "a_b", "a_b", 1),
            Resource("B_B", "b_b", "b_b", 2)
        };
        Action mutable = () => PostgresOperationalRegisterResourceRepository.EnforceResourceImmutabilityWhenHasMovements(
            RegisterId, existing, false, []);
        Action noExisting = () => PostgresOperationalRegisterResourceRepository.EnforceResourceImmutabilityWhenHasMovements(
            RegisterId, [], true, []);
        Action unchanged = () => PostgresOperationalRegisterResourceRepository.EnforceResourceImmutabilityWhenHasMovements(
            RegisterId, existing, true, [Definition("A_B", 1), Definition("B_B", 2)]);
        mutable.Should().NotThrow();
        noExisting.Should().NotThrow();
        unchanged.Should().NotThrow();

        Action removed = () => PostgresOperationalRegisterResourceRepository.EnforceResourceImmutabilityWhenHasMovements(
            RegisterId, existing, true, []);
        var removeError = removed.Should().Throw<OperationalRegisterResourcesAppendOnlyViolationException>().Which;
        removeError.Reason.Should().Be("remove");
        removeError.Context["removedColumnCodes"].Should().BeEquivalentTo(new[] { "a_b", "b_b" });

        Action renamed = () => PostgresOperationalRegisterResourceRepository.EnforceResourceImmutabilityWhenHasMovements(
            RegisterId, existing, true, [Definition("A-B", 1), Definition("B-B", 2)]);
        var renameError = renamed.Should().Throw<OperationalRegisterResourcesAppendOnlyViolationException>().Which;
        renameError.Reason.Should().Be("rename");
        renameError.Context["changes"].Should().BeAssignableTo<string[]>().Which.Should().HaveCount(2);
    }

    [Fact]
    public void Duplicate_validation_covers_empty_inconsistent_non_positive_ordinal_and_collision_groups()
    {
        Action empty = () => PostgresOperationalRegisterResourceRepository.ValidateNoDuplicates(RegisterId, [], [], []);
        empty.Should().NotThrow();

        AssertValidation(
            () => PostgresOperationalRegisterResourceRepository.ValidateNoDuplicates(
                RegisterId, [Definition("a", 1)], [], ["a"]),
            "normalization_inconsistent");
        AssertValidation(
            () => PostgresOperationalRegisterResourceRepository.ValidateNoDuplicates(
                RegisterId, [Definition("a", 1)], ["a"], []),
            "normalization_inconsistent");

        AssertValidation(
            () => PostgresOperationalRegisterResourceRepository.ValidateNoDuplicates(
                RegisterId,
                [Definition("z", 0), Definition("a", -1)],
                ["z", "a"],
                ["z", "a"]),
            "non_positive_ordinal");

        var four = new[]
        {
            Definition("a", 2), Definition("b", 2), Definition("c", 1), Definition("d", 1)
        };
        AssertValidation(
            () => PostgresOperationalRegisterResourceRepository.ValidateNoDuplicates(
                RegisterId, four, ["a", "b", "c", "d"], ["a", "b", "c", "d"]),
            "duplicate_ordinal");

        var uniqueOrdinals = new[]
        {
            Definition("a", 1), Definition("b", 2), Definition("c", 3), Definition("d", 4)
        };
        AssertValidation(
            () => PostgresOperationalRegisterResourceRepository.ValidateNoDuplicates(
                RegisterId, uniqueOrdinals, ["x", "x", "y", "y"], ["a", "b", "c", "d"]),
            "code_norm_collisions");
        AssertValidation(
            () => PostgresOperationalRegisterResourceRepository.ValidateNoDuplicates(
                RegisterId, uniqueOrdinals, ["a", "b", "c", "d"], ["x", "x", "y", "y"]),
            "column_code_collisions");

        Action valid = () => PostgresOperationalRegisterResourceRepository.ValidateNoDuplicates(
            RegisterId, uniqueOrdinals, ["a", "b", "c", "d"], ["a", "b", "c", "d"]);
        valid.Should().NotThrow();
    }

    [Fact]
    public void Reserved_column_validation_accepts_normal_columns_and_reports_sorted_distinct_conflicts()
    {
        Action valid = () => PostgresOperationalRegisterResourceRepository.ValidateNoReservedColumnConflicts(
            RegisterId, ["amount", "quantity"]);
        valid.Should().NotThrow();

        Action invalid = () => PostgresOperationalRegisterResourceRepository.ValidateNoReservedColumnConflicts(
            RegisterId, ["period_month", "document_id", "period_month"]);
        var error = invalid.Should().Throw<OperationalRegisterResourcesValidationException>().Which;
        error.Reason.Should().Be("reserved_column_code");
        error.Context["columnCodes"].Should().BeEquivalentTo(new[] { "document_id", "period_month" });
    }

    private static void AssertValidation(Action action, string reason)
        => action.Should().Throw<OperationalRegisterResourcesValidationException>().Which.Reason.Should().Be(reason);

    private static OperationalRegisterResourceDefinition Definition(string code, int ordinal)
        => new(code, code, ordinal);

    private static OperationalRegisterResource Resource(
        string code,
        string codeNorm,
        string columnCode,
        int ordinal)
        => new(code, codeNorm, columnCode, code, ordinal);

    private static RepositoryFixture Fixture(
        bool? hasMovements,
        IReadOnlyList<OperationalRegisterResource>? existing = null)
        => new(hasMovements, existing ?? []);

    private sealed class RepositoryFixture(
        bool? hasMovements,
        IReadOnlyList<OperationalRegisterResource> existing)
    {
        public RecordingDbConnection Connection { get; } = new(
            readerFactory: _ => ResourceRows(existing),
            scalar: _ => hasMovements);

        public PostgresOperationalRegisterResourceRepository Repository => new(
            new RecordingUnitOfWork(Connection, hasActiveTransaction: true));
    }

    private static System.Data.Common.DbDataReader ResourceRows(IReadOnlyList<OperationalRegisterResource> rows)
    {
        var table = new DataTable();
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("CodeNorm", typeof(string));
        table.Columns.Add("ColumnCode", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Ordinal", typeof(int));
        foreach (var row in rows)
            table.Rows.Add(row.Code, row.CodeNorm, row.ColumnCode, row.Name, row.Ordinal);
        return table.CreateDataReader();
    }
}
