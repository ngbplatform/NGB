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
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: ApplyMovementsForDocumentAsync must fully support external transaction mode:
/// - when manageTransaction=false and no ambient transaction exists => fail fast with canonical message,
/// - when manageTransaction=false and a transaction exists => use it and must not commit/rollback implicitly.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterMovementsApplier_ExternalTransactionMode_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Jan = new(2026, 1, 1);
    private static readonly DateTime NowUtc = new(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ApplyMovements_WhenManageTransactionFalse_AndNoAmbientTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        var act = () => applier.ApplyMovementsForDocumentAsync(
            registerId,
            documentId,
            OperationalRegisterWriteOperation.Post,
            [
                new OperationalRegisterMovement(
                    DocumentId: documentId,
                    OccurredAtUtc: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                    DimensionSetId: Guid.Empty,
                    Resources: new Dictionary<string, decimal>(StringComparer.Ordinal)
                    {
                        ["amount"] = 1m
                    })
            ],
            affectedPeriods: null,
            manageTransaction: false,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    [Fact]
    public async Task ApplyMovements_WhenManageTransactionFalse_UsesAmbientTransaction_AndDoesNotCommit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var code = "it_opreg_exttx_" + registerId.ToString("N")[..8];
        var documentId = Guid.CreateVersion7();

        await SeedRegisterResourcesAndDocumentAsync(host, registerId, code, documentId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            try
            {
                var result = await applier.ApplyMovementsForDocumentAsync(
                    registerId,
                    documentId,
                    OperationalRegisterWriteOperation.Post,
                    [
                        new OperationalRegisterMovement(
                            DocumentId: documentId,
                            OccurredAtUtc: new DateTime(2026, 1, 15, 1, 0, 0, DateTimeKind.Utc),
                            DimensionSetId: Guid.Empty,
                            Resources: new Dictionary<string, decimal>(StringComparer.Ordinal)
                            {
                                ["amount"] = 123.45m
                            })
                    ],
                    affectedPeriods: null,
                    manageTransaction: false,
                    ct: CancellationToken.None);

                result.Should().Be(OperationalRegisterWriteResult.Executed);

                // Critical: service must not commit/rollback the ambient transaction.
                uow.HasActiveTransaction.Should().BeTrue("external transaction mode must not auto-commit");

                await uow.CommitAsync(CancellationToken.None);
            }
            catch
            {
                try { await uow.RollbackAsync(CancellationToken.None); } catch { /* ignore */ }
                throw;
            }
        }

        // Assert commit actually persisted the movement.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsQueryReader>();

            var rows = await reader.GetByMonthsAsync(
                registerId,
                fromInclusive: Jan,
                toInclusive: Jan,
                dimensions: null,
                dimensionSetId: null,
                documentId: documentId,
                isStorno: false,
                afterMovementId: null,
                limit: 100,
                ct: CancellationToken.None);

            rows.Should().ContainSingle();
            rows[0].DocumentId.Should().Be(documentId);
            rows[0].IsStorno.Should().BeFalse();
            rows[0].Values.Should().ContainKey("amount");
        }
    }

    private static async Task SeedRegisterResourcesAndDocumentAsync(IHost host, Guid registerId, string code, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await regRepo.UpsertAsync(new OperationalRegisterUpsert(registerId, code, "Integration Test Register"), NowUtc, ct);

            await resRepo.ReplaceAsync(registerId, new[]
            {
                new OperationalRegisterResourceDefinition("amount", "Amount", 10)
            }, NowUtc, ct);

            await docs.CreateAsync(new DocumentRecord
            {
                Id = documentId,
                TypeCode = "it_doc",
                Number = null,
                DateUtc = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);
    }
}
