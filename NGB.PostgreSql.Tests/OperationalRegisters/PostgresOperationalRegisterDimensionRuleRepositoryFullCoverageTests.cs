using System.Data;
using FluentAssertions;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.OperationalRegisters;

public sealed class PostgresOperationalRegisterDimensionRuleRepositoryFullCoverageTests
{
    private static readonly Guid RegisterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FirstId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SecondId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid ThirdId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid FourthId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTime NowUtc = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Get_returns_empty_or_maps_rules()
    {
        (await Fixture(false).Repository.GetByRegisterIdAsync(RegisterId, default)).Should().BeEmpty();
        var fixture = Fixture(false, current: [Rule(FirstId, "property", 1, true)]);
        (await fixture.Repository.GetByRegisterIdAsync(RegisterId, default))
            .Should().Equal(Rule(FirstId, "property", 1, true));
    }

    [Fact]
    public async Task Replace_validates_arguments_and_missing_register()
    {
        var sut = Fixture(false).Repository;
        Func<Task> nullRules = () => sut.ReplaceAsync(RegisterId, null!, NowUtc, default);
        Func<Task> local = () => sut.ReplaceAsync(RegisterId, [], DateTime.SpecifyKind(NowUtc, DateTimeKind.Local), default);
        Func<Task> emptyRegister = () => sut.ReplaceAsync(Guid.Empty, [], NowUtc, default);
        await nullRules.Should().ThrowAsync<NgbArgumentRequiredException>();
        await local.Should().ThrowAsync<NgbArgumentInvalidException>();
        await emptyRegister.Should().ThrowAsync<NgbArgumentInvalidException>();

        Func<Task> missing = () => Fixture(null).Repository.ReplaceAsync(RegisterId, [], NowUtc, default);
        await missing.Should().ThrowAsync<OperationalRegisterNotFoundException>();
    }

    [Fact]
    public async Task Without_movements_full_replace_deletes_and_optionally_inserts_normalized_rules()
    {
        var empty = Fixture(false);
        await empty.Repository.ReplaceAsync(RegisterId, [], NowUtc, default);
        empty.Connection.Commands.Should().ContainSingle(command => command.CommandText.Contains("DELETE FROM"));

        var fixture = Fixture(false);
        await fixture.Repository.ReplaceAsync(
            RegisterId,
            [Rule(FirstId, "  property  ", 1, true), Rule(SecondId, "unit", 2, false)],
            NowUtc,
            default);
        fixture.Connection.Commands.Should().Contain(command => command.CommandText.Contains("INSERT INTO platform_dimensions"));
        fixture.Connection.Commands.Last().CommandText.Should().Contain("operational_register_dimension_rules");
    }

    [Theory]
    [InlineData("", "empty_dimension_code")]
    [InlineData("   ", "empty_dimension_code")]
    public async Task Rejects_empty_dimension_codes(string code, string reason)
        => await AssertValidationAsync([Rule(FirstId, code, 1, false)], reason);

    [Fact]
    public async Task Rejects_empty_id_non_positive_ordinal_and_duplicate_ordinal()
    {
        await AssertValidationAsync([Rule(Guid.Empty, "property", 1, false)], "empty_dimension_id");
        await AssertValidationAsync([Rule(FirstId, "property", 0, false)], "non_positive_ordinal");
        await AssertValidationAsync(
            [
                Rule(FirstId, "property", 2, false),
                Rule(SecondId, "unit", 2, false),
                Rule(ThirdId, "department", 1, false),
                Rule(FourthId, "warehouse", 1, false)
            ],
            "duplicate_ordinal");
    }

    [Fact]
    public async Task Duplicate_dimension_id_allows_identical_rule_and_rejects_each_conflict_shape()
    {
        var identical = Fixture(false);
        await identical.Repository.ReplaceAsync(
            RegisterId,
            [Rule(FirstId, "property", 1, false), Rule(FirstId, "property", 1, false)],
            NowUtc,
            default);

        await AssertValidationAsync(
            [Rule(FirstId, "property", 1, false), Rule(FirstId, "property", 2, false)],
            "duplicate_dimension_id_conflict");
        await AssertValidationAsync(
            [Rule(FirstId, "property", 1, false), Rule(FirstId, "property", 1, true)],
            "duplicate_dimension_id_conflict");
        await AssertValidationAsync(
            [Rule(FirstId, "property", 1, false), Rule(FirstId, "building", 1, false)],
            "duplicate_dimension_id_conflict");
    }

    [Fact]
    public async Task Empty_replace_after_movements_returns_for_empty_storage_or_rejects_existing_rules()
    {
        var empty = Fixture(true, existingCount: 0);
        await empty.Repository.ReplaceAsync(RegisterId, [], NowUtc, default);

        var existing = Fixture(true, existingCount: 2);
        Func<Task> act = () => existing.Repository.ReplaceAsync(RegisterId, [], NowUtc, default);
        var error = await act.Should().ThrowAsync<OperationalRegisterDimensionRulesAppendOnlyViolationException>();
        error.Which.Reason.Should().Be("replace_empty");
    }

    [Fact]
    public async Task After_movements_existing_rules_are_skipped_optional_rules_inserted_and_required_additions_rejected()
    {
        var existingRule = Rule(FirstId, "property", 1, true);
        var noAdded = Fixture(true, existingIds: [FirstId]);
        await noAdded.Repository.ReplaceAsync(RegisterId, [existingRule], NowUtc, default);
        noAdded.Connection.Commands.Should().NotContain(command => command.CommandText.Contains("INSERT INTO platform_dimensions"));

        var required = Fixture(true, existingIds: [FirstId]);
        Func<Task> addRequired = () => required.Repository.ReplaceAsync(
            RegisterId,
            [existingRule, Rule(ThirdId, "department", 3, true), Rule(SecondId, "unit", 2, true)],
            NowUtc,
            default);
        var error = await addRequired.Should().ThrowAsync<OperationalRegisterDimensionRulesAppendOnlyViolationException>();
        error.Which.Reason.Should().Be("add_required");

        var optional = Fixture(true, existingIds: [FirstId]);
        await optional.Repository.ReplaceAsync(
            RegisterId,
            [existingRule, Rule(SecondId, "unit", 2, false)],
            NowUtc,
            default);
        optional.Connection.Commands.Should().Contain(command => command.CommandText.Contains("INSERT INTO platform_dimensions"));
        optional.Connection.Commands.Last().CommandText.Should().Contain("operational_register_dimension_rules");
    }

    private static async Task AssertValidationAsync(
        IReadOnlyList<OperationalRegisterDimensionRule> rules,
        string reason)
    {
        Func<Task> act = () => Fixture(false).Repository.ReplaceAsync(RegisterId, rules, NowUtc, default);
        var error = await act.Should().ThrowAsync<OperationalRegisterDimensionRulesValidationException>();
        error.Which.Reason.Should().Be(reason);
    }

    private static OperationalRegisterDimensionRule Rule(Guid id, string code, int ordinal, bool required)
        => new(id, code, ordinal, required);

    private static RepositoryFixture Fixture(
        bool? hasMovements,
        IReadOnlyList<OperationalRegisterDimensionRule>? current = null,
        IReadOnlyList<Guid>? existingIds = null,
        int existingCount = 0)
        => new(hasMovements, current ?? [], existingIds ?? [], existingCount);

    private sealed class RepositoryFixture(
        bool? hasMovements,
        IReadOnlyList<OperationalRegisterDimensionRule> current,
        IReadOnlyList<Guid> existingIds,
        int existingCount)
    {
        public RecordingDbConnection Connection { get; } = new(
            readerFactory: sql => sql.Contains("SELECT dimension_id FROM", StringComparison.Ordinal)
                ? GuidRows(existingIds)
                : RuleRows(current),
            scalar: sql => sql.Contains("COUNT(1)", StringComparison.Ordinal) ? existingCount : hasMovements);

        public PostgresOperationalRegisterDimensionRuleRepository Repository => new(
            new RecordingUnitOfWork(Connection, hasActiveTransaction: true));
    }

    private static System.Data.Common.DbDataReader RuleRows(IReadOnlyList<OperationalRegisterDimensionRule> rows)
    {
        var table = new DataTable();
        table.Columns.Add("DimensionId", typeof(Guid));
        table.Columns.Add("DimensionCode", typeof(string));
        table.Columns.Add("Ordinal", typeof(int));
        table.Columns.Add("IsRequired", typeof(bool));
        foreach (var row in rows)
            table.Rows.Add(row.DimensionId, row.DimensionCode, row.Ordinal, row.IsRequired);
        return table.CreateDataReader();
    }

    private static System.Data.Common.DbDataReader GuidRows(IReadOnlyList<Guid> rows)
    {
        var table = new DataTable();
        table.Columns.Add("DimensionId", typeof(Guid));
        foreach (var row in rows) table.Rows.Add(row);
        return table.CreateDataReader();
    }
}
