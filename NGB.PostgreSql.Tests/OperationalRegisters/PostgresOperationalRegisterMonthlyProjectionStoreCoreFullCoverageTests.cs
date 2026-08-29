using System.Data;
using FluentAssertions;
using Moq;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.OperationalRegisters;

public sealed class PostgresOperationalRegisterMonthlyProjectionStoreCoreFullCoverageTests
{
    private static readonly Guid RegisterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FirstSetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SecondSetId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateOnly Period = new(2026, 8, 17);

    [Fact]
    public async Task Public_operations_validate_required_arguments_and_missing_register()
    {
        var sut = Fixture().Store;
        Func<Task> ensureEmptyId = () => sut.EnsureSchemaAsync(Guid.Empty);
        Func<Task> replaceNullRows = () => sut.ReplaceForMonthAsync(RegisterId, Period, null!);
        Func<Task> replaceEmptyId = () => sut.ReplaceForMonthAsync(Guid.Empty, Period, []);
        Func<Task> getEmptyId = () => sut.GetByMonthAsync(Guid.Empty, Period);
        await ensureEmptyId.Should().ThrowAsync<NgbArgumentInvalidException>();
        await replaceNullRows.Should().ThrowAsync<NgbArgumentRequiredException>();
        await replaceEmptyId.Should().ThrowAsync<NgbArgumentInvalidException>();
        await getEmptyId.Should().ThrowAsync<NgbArgumentInvalidException>();

        var missing = Fixture(registerExists: false).Store;
        Func<Task> ensureMissing = () => missing.EnsureSchemaAsync(RegisterId);
        Func<Task> getMissing = () => missing.GetByMonthAsync(RegisterId, Period);
        await ensureMissing.Should().ThrowAsync<OperationalRegisterNotFoundException>();
        await getMissing.Should().ThrowAsync<OperationalRegisterNotFoundException>();
    }

    [Fact]
    public async Task EnsureSchema_creates_table_sorted_resource_columns_and_stable_indexes()
    {
        var resources = new[] { Resource("amount", 2), Resource("quantity", 1) };
        var fixture = Fixture(resources: resources);

        await fixture.Store.EnsureSchemaAsync(RegisterId);

        var ddl = fixture.Connection.Commands.Should().ContainSingle(command =>
            command.CommandText.Contains("CREATE TABLE IF NOT EXISTS opreg_sales_turnovers")).Which.CommandText;
        ddl.Should().Contain("ADD COLUMN IF NOT EXISTS quantity")
            .And.Contain("ADD COLUMN IF NOT EXISTS amount");
        ddl.Split("CREATE INDEX IF NOT EXISTS", StringSplitOptions.None).Length.Should().Be(3);
    }

    [Fact]
    public async Task Ready_check_uses_existing_table_marker_and_reuses_transaction_scoped_shape()
    {
        var fixture = Fixture(
            resources: [Resource("amount", 1)],
            tableExists: true,
            registerHasMovements: true);
        var store = fixture.Store;

        await store.EnsureReadyForWriteAsync(RegisterId);
        await store.EnsureReadyForWriteAsync(RegisterId);

        fixture.Connection.Commands.Should().ContainSingle(command =>
            command.CommandText.Contains("to_regclass", StringComparison.Ordinal));
        fixture.Connection.Commands.Should().NotContain(command =>
            command.CommandText.Contains("CREATE TABLE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ready_check_repairs_schema_when_durable_table_is_missing()
    {
        var fixture = Fixture(tableExists: false, registerHasMovements: true);

        await fixture.Store.EnsureReadyForWriteAsync(RegisterId);

        fixture.Connection.Commands.Should().Contain(command =>
            command.CommandText.Contains("CREATE TABLE IF NOT EXISTS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Replace_handles_empty_rows_no_resources_unknown_keys_and_sparse_resource_arrays()
    {
        var emptyRows = Fixture(resources: [Resource("amount", 1)]);
        await emptyRows.Store.ReplaceForMonthAsync(RegisterId, Period, []);
        emptyRows.Connection.Commands.Should().Contain(command => command.CommandText.Contains("DELETE FROM opreg_sales_turnovers"));
        emptyRows.Connection.Commands.Should().NotContain(command => command.CommandText.Contains("FROM UNNEST(@DimensionSetIds"));

        var noResources = Fixture(resources: []);
        await noResources.Store.ReplaceForMonthAsync(
            RegisterId,
            Period,
            [new OperationalRegisterMonthlyProjectionRow(FirstSetId, new Dictionary<string, decimal>())]);
        noResources.Connection.Commands.Should().Contain(
            command => command.CommandText.Contains("FROM UNNEST(")
                       && command.CommandText.Contains("uuid[]")
                       && !command.CommandText.Contains("numeric[]"));

        var resources = new[] { Resource("quantity", 1), Resource("amount", 2) };
        var invalid = Fixture(resources: resources);
        Func<Task> unknown = () => invalid.Store.ReplaceForMonthAsync(
            RegisterId,
            Period,
            [new OperationalRegisterMonthlyProjectionRow(FirstSetId, new Dictionary<string, decimal> { ["unknown"] = 1m })]);
        var error = await unknown.Should().ThrowAsync<OperationalRegisterResourcesValidationException>();
        error.Which.Context["rowIndex"].Should().Be(0);

        var sparse = Fixture(resources: resources);
        await sparse.Store.ReplaceForMonthAsync(
            RegisterId,
            Period,
            [
                new OperationalRegisterMonthlyProjectionRow(
                    FirstSetId,
                    new Dictionary<string, decimal> { ["quantity"] = 3m, ["amount"] = 12.5m }),
                new OperationalRegisterMonthlyProjectionRow(
                    SecondSetId,
                    new Dictionary<string, decimal> { ["amount"] = 7m })
            ]);
        var insert = sparse.Connection.Commands.Last();
        insert.CommandText.Should().Contain("(@p_quantity1,@p_quantity2)::numeric[]");
        insert.CommandText.Should().Contain("(@p_amount1,@p_amount2)::numeric[]");
        insert.ParametersSnapshot
            .Where(parameter => parameter.ParameterName.StartsWith("p_quantity", StringComparison.Ordinal))
            .Select(parameter => Convert.ToDecimal(parameter.Value))
            .Should().Equal(3m, 0m);
        insert.ParametersSnapshot
            .Where(parameter => parameter.ParameterName.StartsWith("p_amount", StringComparison.Ordinal))
            .Select(parameter => Convert.ToDecimal(parameter.Value))
            .Should().Equal(12.5m, 7m);
    }

    [Fact]
    public async Task Get_returns_empty_when_table_is_absent_and_maps_missing_null_and_numeric_resource_values()
    {
        var resources = new[] { Resource("quantity", 1), Resource("amount", 2) };
        var absent = Fixture(resources: resources, tableExists: false);
        (await absent.Store.GetByMonthAsync(RegisterId, Period)).Should().BeEmpty();

        var rows = new[]
        {
            new Dictionary<string, object?>
            {
                ["DimensionSetId"] = FirstSetId,
                ["quantity"] = 4,
                ["amount"] = null
            },
            new Dictionary<string, object?>
            {
                ["DimensionSetId"] = SecondSetId,
                ["quantity"] = 2.5m
            }
        };
        var fixture = Fixture(resources: resources, rows: rows, aliasResourceColumns: true);

        var result = await fixture.Store.GetByMonthAsync(RegisterId, Period, FirstSetId);

        result.Should().HaveCount(2);
        result[0].Values.Should().Contain(new KeyValuePair<string, decimal>("quantity", 4m));
        result[0].Values.Should().Contain(new KeyValuePair<string, decimal>("amount", 0m));
        result[1].Values.Should().Contain(new KeyValuePair<string, decimal>("amount", 0m));
        fixture.Connection.Commands.Last().CommandText.Should().Contain("quantity AS \"quantity\"");

        var noResources = Fixture(resources: [], rows: [new Dictionary<string, object?> { ["DimensionSetId"] = FirstSetId }]);
        (await noResources.Store.GetByMonthAsync(RegisterId, Period)).Should().ContainSingle();
        noResources.Connection.Commands.Last().CommandText.Should().NotContain("AS \"quantity\"");

        var noAliases = Fixture(resources: [Resource("quantity", 1)], rows: rows, aliasResourceColumns: false);
        await noAliases.Store.GetByMonthAsync(RegisterId, Period);
        noAliases.Connection.Commands.Last().CommandText.Should().Contain("\"DimensionSetId\", quantity");

        var missingColumn = Fixture(
            resources: [Resource("amount", 1)],
            rows: [new Dictionary<string, object?> { ["DimensionSetId"] = FirstSetId }]);
        var missingColumnResult = await missingColumn.Store.GetByMonthAsync(RegisterId, Period);
        missingColumnResult.Single().Values["amount"].Should().Be(0);
    }

    [Fact]
    public async Task Unsafe_generated_table_and_resource_identifiers_fail_fast()
    {
        var badTable = Fixture(tableNameFactory: _ => "bad-table").Store;
        Func<Task> badTableName = () => badTable.GetByMonthAsync(RegisterId, Period);
        await badTableName.Should().ThrowAsync<NgbConfigurationViolationException>();

        var badResource = Fixture(resources: [new("bad", "bad", "bad-column", "Bad", 1)]).Store;
        Func<Task> badColumn = () => badResource.GetByMonthAsync(RegisterId, Period);
        await badColumn.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    private static OperationalRegisterResource Resource(string columnCode, int ordinal)
        => new(columnCode, columnCode, columnCode, columnCode, ordinal);

    private static FixtureState Fixture(
        bool registerExists = true,
        IReadOnlyList<OperationalRegisterResource>? resources = null,
        bool tableExists = true,
        IReadOnlyList<IReadOnlyDictionary<string, object?>>? rows = null,
        bool aliasResourceColumns = false,
        Func<string, string>? tableNameFactory = null,
        bool registerHasMovements = false)
        => new(
            registerExists,
            resources ?? [],
            tableExists,
            rows ?? [],
            aliasResourceColumns,
            tableNameFactory ?? (tableCode => $"opreg_{tableCode}_turnovers"),
            registerHasMovements);

    private sealed class FixtureState(
        bool registerExists,
        IReadOnlyList<OperationalRegisterResource> resources,
        bool tableExists,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        bool aliasResourceColumns,
        Func<string, string> tableNameFactory,
        bool registerHasMovements)
    {
        private readonly Mock<IOperationalRegisterRepository> _registers = new();
        private readonly Mock<IOperationalRegisterResourceRepository> _resources = new();

        public RecordingDbConnection Connection { get; } = new(
            readerFactory: _ => Rows(rows),
            scalar: _ => tableExists);

        public PostgresOperationalRegisterMonthlyProjectionStoreCore Store
        {
            get
            {
                _registers
                    .Setup(repository => repository.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(registerExists ? Register(registerHasMovements) : null);
                _resources
                    .Setup(repository => repository.GetByRegisterIdAsync(RegisterId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(resources);

                return new PostgresOperationalRegisterMonthlyProjectionStoreCore(
                    new RecordingUnitOfWork(Connection, hasActiveTransaction: true),
                    _registers.Object,
                    _resources.Object,
                    tableNameFactory,
                    "test monthly projection table",
                    "ix_test_",
                    aliasResourceColumns);
            }
        }
    }

    private static OperationalRegisterAdminItem Register(bool hasMovements = false)
        => new(RegisterId, "Sales", "sales", "sales", "Sales", hasMovements, DateTime.UnixEpoch, DateTime.UnixEpoch);

    private static System.Data.Common.DbDataReader Rows(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var table = new DataTable();
        foreach (var column in rows.SelectMany(row => row.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            table.Columns.Add(column, typeof(object));
        foreach (var values in rows)
        {
            var row = table.NewRow();
            foreach (DataColumn column in table.Columns)
                row[column] = values.TryGetValue(column.ColumnName, out var value) && value is not null
                    ? value
                    : DBNull.Value;
            table.Rows.Add(row);
        }
        return table.CreateDataReader();
    }
}
