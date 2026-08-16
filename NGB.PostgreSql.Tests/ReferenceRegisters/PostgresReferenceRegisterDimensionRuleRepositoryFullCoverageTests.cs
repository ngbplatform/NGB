using System.Data;
using FluentAssertions;
using NGB.PostgreSql.ReferenceRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.ReferenceRegisters;

public sealed class PostgresReferenceRegisterDimensionRuleRepositoryFullCoverageTests
{
    private static readonly Guid RegisterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FirstDimensionId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SecondDimensionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime NowUtc = new(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Get_returns_empty_or_maps_ordered_rules()
    {
        var empty = Fixture(false, []);
        (await empty.Repository.GetByRegisterIdAsync(RegisterId, default)).Should().BeEmpty();

        var fixture = Fixture(false,
        [
            Rule(FirstDimensionId, "property", 0, true),
            Rule(SecondDimensionId, "unit", 1, false)
        ]);
        var rows = await fixture.Repository.GetByRegisterIdAsync(RegisterId, default);

        rows.Should().Equal(
            Rule(FirstDimensionId, "property", 0, true),
            Rule(SecondDimensionId, "unit", 1, false));
    }

    [Fact]
    public async Task Replace_validates_arguments_and_missing_register()
    {
        var fixture = Fixture(false, []);
        Func<Task> nullRules = () => fixture.Repository.ReplaceAsync(RegisterId, null!, NowUtc, default);
        Func<Task> localTime = () => fixture.Repository.ReplaceAsync(
            RegisterId, [], DateTime.SpecifyKind(NowUtc, DateTimeKind.Local), default);
        Func<Task> emptyRegister = () => fixture.Repository.ReplaceAsync(Guid.Empty, [], NowUtc, default);
        await nullRules.Should().ThrowAsync<NgbArgumentRequiredException>();
        await localTime.Should().ThrowAsync<NgbArgumentInvalidException>();
        await emptyRegister.Should().ThrowAsync<NgbArgumentInvalidException>();

        var missing = Fixture(null, []);
        Func<Task> notFound = () => missing.Repository.ReplaceAsync(RegisterId, [], NowUtc, default);
        await notFound.Should().ThrowAsync<ReferenceRegisterNotFoundException>();
    }

    [Fact]
    public async Task Replace_without_records_deletes_and_optionally_inserts_complete_rule_set()
    {
        var empty = Fixture(false, []);
        await empty.Repository.ReplaceAsync(RegisterId, [], NowUtc, default);
        empty.Connection.Commands.Should().ContainSingle(command =>
            command.CommandText.Contains("DELETE FROM reference_register_dimension_rules", StringComparison.Ordinal));

        var fixture = Fixture(false, []);
        await fixture.Repository.ReplaceAsync(
            RegisterId,
            [Rule(FirstDimensionId, "property", 0, true), Rule(SecondDimensionId, "unit", 1, false)],
            NowUtc,
            default);

        fixture.Connection.Commands.Should().Contain(command => command.CommandText.Contains("DELETE FROM"));
        fixture.Connection.Commands.Should().Contain(command => command.CommandText.Contains("INSERT INTO platform_dimensions"));
        fixture.Connection.Commands.Should().Contain(command =>
            command.CommandText.Contains("INSERT INTO reference_register_dimension_rules")
            && !command.CommandText.Contains("ON CONFLICT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Append_only_empty_current_set_allows_nothing_or_optional_additions_but_rejects_required()
    {
        var noChange = Fixture(true, []);
        await noChange.Repository.ReplaceAsync(RegisterId, [], NowUtc, default);
        noChange.Connection.Commands.Should().ContainSingle(command => command.CommandText.Contains("SELECT has_records"));

        var required = Fixture(true, []);
        Func<Task> addRequired = () => required.Repository.ReplaceAsync(
            RegisterId,
            [Rule(SecondDimensionId, "unit", 1, true), Rule(FirstDimensionId, "property", 0, true)],
            NowUtc,
            default);
        var requiredError = await addRequired.Should()
            .ThrowAsync<ReferenceRegisterDimensionRulesAppendOnlyViolationException>();
        requiredError.Which.Reason.Should().Be("add_required_dimension");

        var optional = Fixture(true, []);
        await optional.Repository.ReplaceAsync(
            RegisterId,
            [Rule(FirstDimensionId, "property", 0, false)],
            NowUtc,
            default);
        optional.Connection.Commands.Should().Contain(command => command.CommandText.Contains("INSERT INTO platform_dimensions"));
        optional.Connection.Commands.Should().Contain(command =>
            command.CommandText.Contains("INSERT INTO reference_register_dimension_rules")
            && command.CommandText.Contains("ON CONFLICT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Append_only_existing_rules_reject_removal_ordinal_and_required_changes()
    {
        var current = new[] { Rule(FirstDimensionId, "property", 0, false) };

        var remove = Fixture(true, current);
        await AssertViolationAsync(remove, [], "remove_dimension");

        var ordinal = Fixture(true, current);
        await AssertViolationAsync(ordinal, [Rule(FirstDimensionId, "property", 5, false)], "change_ordinal");

        var required = Fixture(true, current);
        await AssertViolationAsync(required, [Rule(FirstDimensionId, "property", 0, true)], "change_required");
    }

    [Fact]
    public async Task Append_only_existing_rules_allow_no_change_or_optional_addition_and_reject_required_addition()
    {
        var existing = Rule(FirstDimensionId, "property", 0, false);
        var noChange = Fixture(true, [existing]);
        await noChange.Repository.ReplaceAsync(RegisterId, [existing], NowUtc, default);
        noChange.Connection.Commands.Should().NotContain(command => command.CommandText.Contains("INSERT INTO platform_dimensions"));

        var required = Fixture(true, [existing]);
        await AssertViolationAsync(
            required,
            [
                existing,
                Rule(SecondDimensionId, "unit", 1, true),
                Rule(Guid.Parse("44444444-4444-4444-4444-444444444444"), "department", 2, true)
            ],
            "add_required_dimension");

        var optional = Fixture(true, [existing]);
        await optional.Repository.ReplaceAsync(
            RegisterId,
            [existing, Rule(SecondDimensionId, "unit", 1, false)],
            NowUtc,
            default);
        optional.Connection.Commands.Should().Contain(command => command.CommandText.Contains("INSERT INTO platform_dimensions"));
        optional.Connection.Commands.Last().CommandText.Should().Contain("ON CONFLICT");
    }

    private static async Task AssertViolationAsync(
        RepositoryFixture fixture,
        IReadOnlyList<ReferenceRegisterDimensionRule> next,
        string reason)
    {
        Func<Task> act = () => fixture.Repository.ReplaceAsync(RegisterId, next, NowUtc, default);
        var error = await act.Should().ThrowAsync<ReferenceRegisterDimensionRulesAppendOnlyViolationException>();
        error.Which.Reason.Should().Be(reason);
    }

    private static ReferenceRegisterDimensionRule Rule(Guid id, string code, int ordinal, bool required)
        => new(id, code, ordinal, required);

    private static RepositoryFixture Fixture(
        bool? hasRecords,
        IReadOnlyList<ReferenceRegisterDimensionRule> current)
        => new(hasRecords, current);

    private sealed class RepositoryFixture(
        bool? hasRecords,
        IReadOnlyList<ReferenceRegisterDimensionRule> current)
    {
        public RecordingDbConnection Connection { get; } = new(
            readerFactory: _ => RuleRows(current),
            scalar: _ => hasRecords);

        public PostgresReferenceRegisterDimensionRuleRepository Repository => new(
            new RecordingUnitOfWork(Connection, hasActiveTransaction: true));
    }

    private static System.Data.Common.DbDataReader RuleRows(
        IReadOnlyList<ReferenceRegisterDimensionRule> rows)
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
}
