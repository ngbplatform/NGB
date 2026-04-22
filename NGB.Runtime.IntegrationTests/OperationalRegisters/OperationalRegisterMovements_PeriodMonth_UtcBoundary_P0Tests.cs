using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.OperationalRegisters;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using System.Globalization;
using NGB.OperationalRegisters.Contracts;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterMovements_PeriodMonth_UtcBoundary_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PeriodMonth_IsComputedInUtc_EvenWhenSessionTimeZoneWouldMapToPreviousMonth()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var code = "tz_boundary";

        await SeedRegisterAndDocumentAsync(host, registerId, code, documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        // This UTC timestamp is Feb 1, but in America/New_York it is still Jan 31 evening.
        var occurredAtUtc = new DateTime(2026, 2, 1, 0, 30, 0, DateTimeKind.Utc);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            // Ensure session time zone does not influence period_month calculation.
            await uow.Connection.ExecuteAsync("SELECT set_config('TimeZone', @tz, false);", new { tz = "America/New_York" });

            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var movements = new[]
            {
                new OperationalRegisterMovement(documentId,
                    occurredAtUtc,
                    Guid.Empty,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 10m })
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

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var table = OperationalRegisterNaming.MovementsTable(code);

            // Dapper.AOT: avoid `object` / dynamic materialization.
            var periodMonthText = await uow.Connection.ExecuteScalarAsync<string?>(
                $"SELECT (period_month::date)::text FROM {table} WHERE document_id = @D AND is_storno = FALSE;",
                new { D = documentId });

            var asDateOnly = DateOnly.ParseExact(
                periodMonthText ?? throw new InvalidOperationException($"{table}.period_month was null."),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);
            asDateOnly.Should().Be(new DateOnly(2026, 2, 1));
        }
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
            new OperationalRegisterUpsert(registerId, registerCode, "Tz Boundary"),
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
