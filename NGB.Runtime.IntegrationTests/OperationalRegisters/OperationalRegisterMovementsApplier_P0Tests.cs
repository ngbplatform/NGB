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
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: End-to-end semantics for applying movements over the canonical write pipeline.
/// Ensures:
/// - per-register movements table is created (EnsureSchema)
/// - semantics: append-only + storno (Unpost/Repost)
/// - dirty months are derived when affectedPeriods is null
/// - idempotency and atomicity (rollback undoes log + dirty markers + rows)
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterMovementsApplier_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Post_WhenBegun_AppendsRows_MarksDerivedMonthsDirty_AndCompletesLog()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var code = "rent_roll";

        await SeedRegisterAndDocumentAsync(host, registerId, code, documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        var movements = new[]
        {
            new OperationalRegisterMovement(documentId,
                new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 10m }),
            new OperationalRegisterMovement(documentId,
                new DateTime(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 20m })
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
            var finalizations = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var table = OperationalRegisterNaming.MovementsTable(code);

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var rows = (await uow.Connection.QueryAsync<MovementRow>(new CommandDefinition(
                $"""
                SELECT
                    document_id     AS DocumentId,
                    occurred_at_utc  AS OccurredAtUtc,
                    period_month     AS PeriodMonth,
                    dimension_set_id AS DimensionSetId,
                    is_storno        AS IsStorno,
                    amount           AS Amount
                FROM {table}
                WHERE document_id = @D
                ORDER BY occurred_at_utc;
                """,
                new { D = documentId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None))).AsList();

            rows.Should().HaveCount(2);
            rows[0].PeriodMonth.Should().Be(new DateOnly(2026, 1, 1));
            rows[1].PeriodMonth.Should().Be(new DateOnly(2026, 2, 1));
            rows[0].IsStorno.Should().BeFalse();
            rows[1].IsStorno.Should().BeFalse();
            rows[0].Amount.Should().Be(10m);
            rows[1].Amount.Should().Be(20m);

            var jan = await finalizations.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None);
            var feb = await finalizations.GetAsync(registerId, new DateOnly(2026, 2, 1), CancellationToken.None);

            jan.Should().NotBeNull();
            jan!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);
            feb.Should().NotBeNull();
            feb!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);

            var completedAtUtc = await uow.Connection.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
                """
                SELECT completed_at_utc
                FROM operational_register_write_state
                WHERE register_id = @R AND document_id = @D AND operation = @O;
                """,
                new { R = registerId, D = documentId, O = (short)OperationalRegisterWriteOperation.Post },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            completedAtUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Post_WhenCalledTwice_IsStrictlyIdempotent_AndDoesNotMutateRows()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var code = "rent_roll";

        await SeedRegisterAndDocumentAsync(host, registerId, code, documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        var table = OperationalRegisterNaming.MovementsTable(code);

        var movementsV1 = new[]
        {
            new OperationalRegisterMovement(documentId,
                new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 1m }),
            new OperationalRegisterMovement(documentId,
                new DateTime(2026, 1, 11, 10, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 2m })
        };

        var movementsV2 = new[]
        {
            new OperationalRegisterMovement(documentId,
                new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 111m }),
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var r1 = await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                movementsV1,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            var r2 = await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                movementsV2,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            r1.Should().Be(OperationalRegisterWriteResult.Executed);
            r2.Should().Be(OperationalRegisterWriteResult.AlreadyCompleted);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var count = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                $"SELECT count(*) FROM {table} WHERE document_id = @D;",
                new { D = documentId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            count.Should().Be(2);

            var amounts = (await uow.Connection.QueryAsync<decimal>(new CommandDefinition(
                $"SELECT amount FROM {table} WHERE document_id = @D AND is_storno = FALSE ORDER BY occurred_at_utc;",
                new { D = documentId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None))).AsList();

            amounts.Should().HaveCount(2);
            amounts[0].Should().Be(1m);
            amounts[1].Should().Be(2m);
        }
    }

    [Fact]
    public async Task Unpost_WithExplicitAffectedPeriods_AppendsStornoRows_AndMarksMonthsDirty()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var code = "rent_roll";

        await SeedRegisterAndDocumentAsync(host, registerId, code, documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });
        var table = OperationalRegisterNaming.MovementsTable(code);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var movements = new[]
            {
                new OperationalRegisterMovement(documentId,
                    new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc),
                    Guid.Empty,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 1m })
            };

            (await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                movements,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None)).Should().Be(OperationalRegisterWriteResult.Executed);

            (await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Unpost,
                Array.Empty<OperationalRegisterMovement>(),
                affectedPeriods: new[] { new DateOnly(2026, 1, 1) },
                manageTransaction: true,
                ct: CancellationToken.None)).Should().Be(OperationalRegisterWriteResult.Executed);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var finalizations = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var count = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                $"SELECT count(*) FROM {table} WHERE document_id = @D;",
                new { D = documentId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            // Unpost is represented by an appended storno movement.
            count.Should().Be(2);

            var jan = await finalizations.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None);
            jan.Should().NotBeNull();
            jan!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);

            var unpostCompletedAtUtc = await uow.Connection.QuerySingleOrDefaultAsync<DateTime?>(new CommandDefinition(
                """
                SELECT completed_at_utc
                FROM operational_register_write_state
                WHERE register_id = @R AND document_id = @D AND operation = @O;
                """,
                new { R = registerId, D = documentId, O = (short)OperationalRegisterWriteOperation.Unpost },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            unpostCompletedAtUtc.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Unpost_WhenAffectedPeriodsIsNull_DerivesMonthsFromExistingMovements_AndMarksFinalizedMonthDirty()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var code = "rent_roll";

        await SeedRegisterAndDocumentAsync(host, registerId, code, documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var movements = new[]
            {
                new OperationalRegisterMovement(documentId,
                    new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc),
                    Guid.Empty,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 1m })
            };

            (await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                movements,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None)).Should().Be(OperationalRegisterWriteResult.Executed);
        }

        // Flip the month to Finalized so that Unpost must mark it Dirty again.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var finalizations = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var jan = new DateOnly(2026, 1, 1);
            var nowUtc = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc);

            await uow.BeginTransactionAsync(CancellationToken.None);
            await finalizations.MarkFinalizedAsync(registerId, jan, finalizedAtUtc: nowUtc, nowUtc: nowUtc, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            (await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Unpost,
                Array.Empty<OperationalRegisterMovement>(),
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None)).Should().Be(OperationalRegisterWriteResult.Executed);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var finalizations = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var jan = await finalizations.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None);
            jan.Should().NotBeNull();
            jan!.Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);

            var table = OperationalRegisterNaming.MovementsTable(code);

            var count = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                $"SELECT count(*) FROM {table} WHERE document_id = @D;",
                new { D = documentId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            count.Should().Be(2);
        }
    }

    [Fact]
    public async Task Repost_WhenAffectedPeriodsIsNull_UsesUnionOfExistingAndNewMonths_AndMarksBothDirty()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var code = "rent_roll";

        await SeedRegisterAndDocumentAsync(host, registerId, code, documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        // Initial post in January.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var movements = new[]
            {
                new OperationalRegisterMovement(documentId,
                    new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc),
                    Guid.Empty,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 1m })
            };

            (await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                movements,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None)).Should().Be(OperationalRegisterWriteResult.Executed);
        }

        // Mark both January and February as finalized. Repost must mark both dirty:
        // - January because we storno the old state
        // - February because we insert new movements
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var finalizations = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            var nowUtc = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc);
            var jan = new DateOnly(2026, 1, 1);
            var feb = new DateOnly(2026, 2, 1);

            await uow.BeginTransactionAsync(CancellationToken.None);
            await finalizations.MarkFinalizedAsync(registerId, jan, finalizedAtUtc: nowUtc, nowUtc: nowUtc, CancellationToken.None);
            await finalizations.MarkFinalizedAsync(registerId, feb, finalizedAtUtc: nowUtc, nowUtc: nowUtc, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        // Repost with a new movement in February only.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var movementsV2 = new[]
            {
                new OperationalRegisterMovement(documentId,
                    new DateTime(2026, 2, 5, 10, 0, 0, DateTimeKind.Utc),
                    Guid.Empty,
                    new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 2m })
            };

            (await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Repost,
                movementsV2,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None)).Should().Be(OperationalRegisterWriteResult.Executed);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var finalizations = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            (await finalizations.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None))
                .Should().NotBeNull();
            (await finalizations.GetAsync(registerId, new DateOnly(2026, 1, 1), CancellationToken.None))!
                .Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);

            (await finalizations.GetAsync(registerId, new DateOnly(2026, 2, 1), CancellationToken.None))
                .Should().NotBeNull();
            (await finalizations.GetAsync(registerId, new DateOnly(2026, 2, 1), CancellationToken.None))!
                .Status.Should().Be(OperationalRegisterFinalizationStatus.Dirty);

            var table = OperationalRegisterNaming.MovementsTable(code);

            var count = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                $"SELECT count(*) FROM {table} WHERE document_id = @D;",
                new { D = documentId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            count.Should().Be(3);
        }
    }

    [Fact]
    public async Task Post_WhenInsertFails_RollsBack_LogDirtyMarkersAndTableAreNotCommitted()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var code = "rent_roll";

        await SeedRegisterAndDocumentAsync(host, registerId, code, documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        });

        var badMovements = new[]
        {
            // DimensionSetId does not exist -> FK violation (platform_dimension_sets)
            new OperationalRegisterMovement(documentId,
                new DateTime(2026, 1, 10, 10, 0, 0, DateTimeKind.Utc),
                Guid.CreateVersion7(),
                new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 1m })
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var act = async () => await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                badMovements,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<Exception>();
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var logCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                SELECT count(*)
                FROM operational_register_write_state
                WHERE register_id = @R AND document_id = @D AND operation = @O;
                """,
                new { R = registerId, D = documentId, O = (short)OperationalRegisterWriteOperation.Post },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            logCount.Should().Be(0);

            var finCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                "SELECT count(*) FROM operational_register_finalizations WHERE register_id = @R;",
                new { R = registerId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            finCount.Should().Be(0);

            var table = OperationalRegisterNaming.MovementsTable(code);
            var exists = await uow.Connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT to_regclass(@T) IS NOT NULL;",
                new { T = table },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            // Schema objects are allowed to exist even when the write itself fails.
            // Atomicity requirement here is: log + dirty markers + rows are not committed.
            exists.Should().BeTrue();

            var rowsCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                $"SELECT count(*) FROM {table};",
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            rowsCount.Should().Be(0);
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

        // Resources define physical numeric columns of per-register tables.
        // Keep it minimal for tests.
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

    private sealed class MovementRow
    {
        public Guid DocumentId { get; init; }
        public DateTime OccurredAtUtc { get; init; }
        public DateOnly PeriodMonth { get; init; }
        public Guid DimensionSetId { get; init; }
        public bool IsStorno { get; init; }
        public decimal Amount { get; init; }
    }
}
