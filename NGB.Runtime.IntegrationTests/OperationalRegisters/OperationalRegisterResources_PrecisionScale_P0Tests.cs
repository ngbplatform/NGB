using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Resource columns must use NUMERIC(28,8) consistently across opreg_*__movements/turnovers/balances.
/// This prevents precision loss during month finalization (turnovers/balances are derived tables).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterResources_PrecisionScale_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ResourceColumns_AreNumeric_28_8_InAllRegisterTables_AndRoundtripEightDecimals()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        const string code = "rent_roll";

        await SeedRegisterAndDocumentAsync(host, registerId, code, documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1),
            new OperationalRegisterResourceDefinition("qty", "Qty", 2)
        });

        var value8dp = 1.12345678m;

        // 1) Movements table: create schema + resource columns.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var movements = new[]
            {
                new OperationalRegisterMovement(
                    documentId,
                    new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
                    Guid.Empty,
                    new Dictionary<string, decimal>(StringComparer.Ordinal)
                    {
                        ["amount"] = value8dp,
                        ["qty"] = 2m
                    })
            };

            var result = await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                movements,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            result.Should().Be(OperationalRegisterWriteResult.Executed);
        }

        // 2) Turnovers/Balances tables: ensure schema + resource columns.
        var periodAny = new DateOnly(2026, 1, 15); // not month-start on purpose
        var rows = new[]
        {
            new OperationalRegisterMonthlyProjectionRow(
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["amount"] = value8dp,
                    ["qty"] = 2m
                })
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            await turnovers.ReplaceForMonthAsync(registerId, periodAny, rows, CancellationToken.None);
            await balances.ReplaceForMonthAsync(registerId, periodAny, rows, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        // 3) Assert DB types via information_schema and verify 8dp roundtrip.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var tableCode = await uow.Connection.ExecuteScalarAsync<string>(
                "select table_code from operational_registers where register_id = @id",
                new { id = registerId });

            tableCode.Should().NotBeNullOrWhiteSpace();

            var movementsTable = $"opreg_{tableCode}__movements";
            var turnoversTable = $"opreg_{tableCode}__turnovers";
            var balancesTable = $"opreg_{tableCode}__balances";

            var amountColumn = await uow.Connection.ExecuteScalarAsync<string>(
                "select column_code from operational_register_resources where register_id = @reg and code_norm = @cn",
                new { reg = registerId, cn = "amount" });

            var qtyColumn = await uow.Connection.ExecuteScalarAsync<string>(
                "select column_code from operational_register_resources where register_id = @reg and code_norm = @cn",
                new { reg = registerId, cn = "qty" });

            amountColumn.Should().NotBeNullOrWhiteSpace();
            qtyColumn.Should().NotBeNullOrWhiteSpace();

            await AssertNumeric28_8Async(uow, movementsTable, amountColumn);
            await AssertNumeric28_8Async(uow, movementsTable, qtyColumn);

            await AssertNumeric28_8Async(uow, turnoversTable, amountColumn);
            await AssertNumeric28_8Async(uow, turnoversTable, qtyColumn);

            await AssertNumeric28_8Async(uow, balancesTable, amountColumn);
            await AssertNumeric28_8Async(uow, balancesTable, qtyColumn);

            // Roundtrip: derived stores should not truncate to 4dp.
            var t = await turnovers.GetByMonthAsync(registerId, periodAny, ct: CancellationToken.None);
            var b = await balances.GetByMonthAsync(registerId, periodAny, ct: CancellationToken.None);

            t.Should().HaveCount(1);
            b.Should().HaveCount(1);

            t[0].Values.TryGetValue("amount", out var tAmount).Should().BeTrue();
            b[0].Values.TryGetValue("amount", out var bAmount).Should().BeTrue();

            tAmount.Should().Be(value8dp);
            bAmount.Should().Be(value8dp);
        }
    }

    private static async Task AssertNumeric28_8Async(IUnitOfWork uow, string tableName, string columnName)
    {
        var row = await uow.Connection.QuerySingleOrDefaultAsync<ColumnTypeRow>(
            """
            select data_type as DataType,
                   numeric_precision as NumericPrecision,
                   numeric_scale as NumericScale
            from information_schema.columns
            where table_schema = 'public'
              and table_name = @table
              and column_name = @col;
            """,
            new { table = tableName, col = columnName });

        row.Should().NotBeNull($"column {tableName}.{columnName} must exist");
        row!.DataType.Should().Be("numeric");
        row.NumericPrecision.Should().Be(28);
        row.NumericScale.Should().Be(8);
    }

    private sealed class ColumnTypeRow
    {
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public string DataType { get; set; } = string.Empty;

        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public int? NumericPrecision { get; set; }

        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public int? NumericScale { get; set; }
    }

    private static async Task SeedRegisterAndDocumentAsync(
        IHost host,
        Guid registerId,
        string registerCode,
        Guid documentId,
        IReadOnlyList<OperationalRegisterResourceDefinition>? resources = null)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);

        await regRepo.UpsertAsync(
            new OperationalRegisterUpsert(registerId, registerCode, "Rent Roll"),
            nowUtc,
            CancellationToken.None);

        if (resources is not null)
            await resRepo.ReplaceAsync(registerId, resources, nowUtc, CancellationToken.None);

        await docs.CreateAsync(new DocumentRecord
        {
            Id = documentId,
            TypeCode = "it_doc",
            Number = null,
            DateUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        }, CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);
    }
}
