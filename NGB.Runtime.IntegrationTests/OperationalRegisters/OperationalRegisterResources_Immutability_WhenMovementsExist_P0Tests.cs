using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: semantics rely on storno rows copied from prior movements.
/// Therefore physical resource columns (column_code) must not disappear after movements exist.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterResources_Immutability_WhenMovementsExist_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ResourceRepository_Replace_WhenMovementsExist_AndResourceIsRemoved_Throws_AndKeepsPreviousResources()
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

        var movements = new[]
        {
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["amount"] = 10m,
                    ["qty"] = 2m
                })
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

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
            var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

            var nowUtc = new DateTime(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc);

            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
                // Remove resource "qty" while movements exist -> must be forbidden.
                var act = () => resRepo.ReplaceAsync(
                    registerId,
                    [
                        new OperationalRegisterResourceDefinition("amount", "Amount", 1)
                    ],
                    nowUtc,
                    CancellationToken.None);

                var ex = await act.Should().ThrowAsync<OperationalRegisterResourcesAppendOnlyViolationException>();
                ex.Which.AssertNgbError(OperationalRegisterResourcesAppendOnlyViolationException.Code, "registerId", "reason");
                ex.Which.AssertReason("remove");
                ex.Which.Context["removedColumnCodes"].Should().BeAssignableTo<IReadOnlyList<string>>();
                ((IReadOnlyList<string>)ex.Which.Context["removedColumnCodes"]!).Should().Contain("qty");
            }
            finally
            {
                if (uow.HasActiveTransaction)
                    await uow.RollbackAsync(CancellationToken.None);
            }

            var existing = await resRepo.GetByRegisterIdAsync(registerId, CancellationToken.None);

            existing.Select(x => x.ColumnCode)
                .Should()
                .BeEquivalentTo(new[] { "amount", "qty" });
        }
    }

    [Fact]
    public async Task ResourceRepository_Replace_WhenMovementsExist_AndResourceIsRenamed_Throws_AndKeepsPreviousResources()
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

        var movements = new[]
        {
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["amount"] = 10m,
                    ["qty"] = 2m
                })
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

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
            var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

            var nowUtc = new DateTime(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc);

            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
                // Renaming 'qty' to a different code changes physical column_code -> forbidden once movements exist.
                var act = () => resRepo.ReplaceAsync(
                    registerId,
                    [
                        new OperationalRegisterResourceDefinition("amount", "Amount", 1),
                        new OperationalRegisterResourceDefinition("quantity", "Quantity", 2)
                    ],
                    nowUtc,
                    CancellationToken.None);

                var ex = await act.Should().ThrowAsync<OperationalRegisterResourcesAppendOnlyViolationException>();
                ex.Which.AssertNgbError(OperationalRegisterResourcesAppendOnlyViolationException.Code, "registerId", "reason");
                ex.Which.AssertReason("remove");
                ex.Which.Context["removedColumnCodes"].Should().BeAssignableTo<IReadOnlyList<string>>();
                ((IReadOnlyList<string>)ex.Which.Context["removedColumnCodes"]!).Should().Contain("qty");
            }
            finally
            {
                if (uow.HasActiveTransaction)
                    await uow.RollbackAsync(CancellationToken.None);
            }

            var existing = await resRepo.GetByRegisterIdAsync(registerId, CancellationToken.None);

            existing.Select(x => x.ColumnCode)
                .Should()
                .BeEquivalentTo(new[] { "amount", "qty" });
        }
    }

    [Fact]
    public async Task ResourceRepository_Replace_WhenMovementsExist_AndResourceIsAdded_Allows_AndUnpostStillWorks()
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

        var movements = new[]
        {
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["amount"] = 10m,
                    ["qty"] = 2m
                })
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

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
            // Adding a new resource after movements exist must be allowed.
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

            var nowUtc = new DateTime(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc);

            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
                await resRepo.ReplaceAsync(
                    registerId,
                    [
                        new OperationalRegisterResourceDefinition("amount", "Amount", 1),
                        new OperationalRegisterResourceDefinition("qty", "Qty", 2),
                        new OperationalRegisterResourceDefinition("price", "Price", 3)
                    ],
                    nowUtc,
                    CancellationToken.None);

                await uow.CommitAsync(CancellationToken.None);
            }
            finally
            {
                if (uow.HasActiveTransaction)
                    await uow.RollbackAsync(CancellationToken.None);
            }
        }

        // And Unpost should still work (EnsureSchema adds the new column; storno copies it with 0 values).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();
            var reader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsReader>();

            var result = await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Unpost,
                Array.Empty<OperationalRegisterMovement>(),
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            result.Should().Be(OperationalRegisterWriteResult.Executed);

            var month = new DateOnly(2026, 1, 1);
            var rows = await reader.GetByMonthAsync(registerId, month, dimensionSetId: null, afterMovementId: null, limit: 1000, ct: CancellationToken.None);

            rows.Should().NotBeEmpty();
            rows.All(r => r.Resources.ContainsKey("price")).Should().BeTrue("new resource column must be readable on all movement rows");
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
            new OperationalRegisterUpsert(registerId, registerCode, "Rent Roll"),
            nowUtc,
            CancellationToken.None);

        if (resources is not null)
        {
            await resRepo.ReplaceAsync(registerId, resources, nowUtc, CancellationToken.None);
        }

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
