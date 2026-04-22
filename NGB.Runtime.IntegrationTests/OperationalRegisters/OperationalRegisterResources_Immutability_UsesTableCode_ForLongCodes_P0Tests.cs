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
/// P0: Ensure Operational Register repositories resolve per-register tables via DB-generated table_code,
/// not via code_norm. This matters because code_norm can exceed PostgreSQL identifier limit (63),
/// while table_code is truncated+hashed to guarantee safe physical table names.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterResources_Immutability_UsesTableCode_ForLongCodes_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ResourceReplace_WhenMovementsExist_ForLongCodeNorm_DoesNotFailOnIdentifierLength_AndThrowsImmutability()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        // Make code_norm > 63 so that using code_norm as table token would violate identifier limit.
        var code = new string('r', 120) + "_tail";
        Guid registerId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
            registerId = await svc.UpsertAsync(code, "Long Code Register", CancellationToken.None);

            await svc.ReplaceResourcesAsync(registerId,
                [
                    new OperationalRegisterResourceDefinition("amount", "Amount", 1),
                    new OperationalRegisterResourceDefinition("qty", "Qty", 2)
                ],
                CancellationToken.None);
        }

        var documentId = Guid.CreateVersion7();
        await SeedDocumentAsync(host, documentId);

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

        // Sanity: code_norm should be long, but DB table_code should be short enough to build safe table names.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
            var reg = await repo.GetByIdAsync(registerId, CancellationToken.None);

            reg.Should().NotBeNull();
            reg!.CodeNorm.Length.Should().BeGreaterThan(63);
            reg.TableCode.Length.Should().BeLessThanOrEqualTo(46);
        }

        // Attempt to remove an existing resource after movements exist.
        // Expected: immutability exception.
        // Regression guard: if the repository used code_norm to build the table name, it would throw "Unsafe identifier ... len > 63".
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

            var nowUtc = new DateTime(2026, 1, 20, 12, 0, 0, DateTimeKind.Utc);

            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
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
                ((IReadOnlyList<string>)ex.Which.Context["removedColumnCodes"]!).Should().Contain("qty");

                ex.Which.Message.Should().NotContain("Unsafe identifier");
            }
            finally
            {
                if (uow.HasActiveTransaction)
                    await uow.RollbackAsync(CancellationToken.None);
            }
        }
    }

    private static async Task SeedDocumentAsync(IHost host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);

        await docs.CreateAsync(new DocumentRecord
        {
            Id = id,
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
