using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.OperationalRegisters.Projections;
using NGB.Runtime.OperationalRegisters.Projections.Examples;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P1: A three-way race between:
/// - admin EnsurePhysicalSchema (dynamic DDL + schema lock),
/// - movements apply (write log + month lock),
/// - finalization runner (month lock + projector writes projections),
/// must not deadlock and must leave a consistent state.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegister_EnsureSchema_Write_Finalize_TripleRace_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TripleRace_EnsureSchema_WriteAndFinalize_DoesNotDeadlock_AndLeavesConsistentState()
    {
        var code = "RR_TRIPLE_" + Guid.CreateVersion7().ToString("N")[..10].ToUpperInvariant();
        var codeNorm = code.Trim().ToLowerInvariant();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<IOperationalRegisterMonthProjector>(sp =>
                    new MovementsCountProjector(
                        registerCodeNorm: codeNorm,
                        turnovers: sp.GetRequiredService<IOperationalRegisterTurnoversStore>(),
                        balances: sp.GetRequiredService<IOperationalRegisterBalancesStore>()));
            });

        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        await SeedRegisterAndDocumentAsync(host, registerId, code, documentId, resources: BuildResources());

        // Ensure finalizers always have work, even if a writer races and finishes later.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationService>();
            await svc.MarkDirtyAsync(
                registerId,
                new DateOnly(2026, 1, 15),
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        var movements = new[]
        {
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal)),
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 1, 11, 12, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal))
        };

        // Act: lots of overlap to exercise lock ordering across:
        // schema locks (DDL) + month locks + write log.
        var tasks = new List<Task>(24);

        for (var i = 0; i < 8; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                await using var scope = host.Services.CreateAsyncScope();
                var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

                var health = await endpoint.EnsurePhysicalSchemaByIdAsync(registerId, CancellationToken.None);
                health.Should().NotBeNull();
                health!.IsOk.Should().BeTrue();
            }));

            tasks.Add(Task.Run(async () =>
            {
                await using var scope = host.Services.CreateAsyncScope();
                var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

                _ = await applier.ApplyMovementsForDocumentAsync(
                    registerId,
                    documentId,
                    OperationalRegisterWriteOperation.Post,
                    movements,
                    affectedPeriods: null,
                    manageTransaction: true,
                    ct: CancellationToken.None);
            }));

            tasks.Add(Task.Run(async () =>
            {
                await using var scope = host.Services.CreateAsyncScope();
                var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();

                _ = await runner.FinalizeRegisterDirtyAsync(
                    registerId,
                    maxPeriods: 10,
                    manageTransaction: true,
                    ct: CancellationToken.None);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert: physical schema is healthy.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var endpoint = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminEndpoint>();

            var health = await endpoint.GetPhysicalSchemaHealthByIdAsync(registerId, CancellationToken.None);
            health.Should().NotBeNull();
            health!.IsOk.Should().BeTrue();
        }

        // Assert: movements are appended exactly once (Post is strictly idempotent by write_log).
        var movementsTable = OperationalRegisterNaming.MovementsTable(codeNorm);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var nonStorno = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                $"SELECT count(*) FROM {movementsTable} WHERE document_id = @D AND is_storno = FALSE;",
                new { D = documentId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            nonStorno.Should().Be(2);
        }

        // Assert: month is NOT blocked (projector is registered); it may be Dirty or Finalized depending on interleaving.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var row = await repo.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None);
            row.Should().NotBeNull();

            row!.Status.Should().NotBe(OperationalRegisterFinalizationStatus.BlockedNoProjector);
        }
    }

    private static IReadOnlyList<OperationalRegisterResourceDefinition> BuildResources()
    {
        var list = new List<OperationalRegisterResourceDefinition>
        {
            new("movement_count", "Movement Count", 1)
        };

        // Add extra resources to make EnsurePhysicalSchema heavier (ALTER TABLE for many columns).
        for (var i = 2; i <= 25; i++)
        {
            var code = $"r{i:00}";
            list.Add(new OperationalRegisterResourceDefinition(code, code.ToUpperInvariant(), i));
        }

        return list;
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
            new OperationalRegisterUpsert(registerId, registerCode, "IT Register"),
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
