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
/// P0: PostgreSQL identifiers are capped at 63 chars. Long resource codes must not silently collide after truncation.
/// We guarantee this by adding a deterministic hash suffix when normalization needs truncation.
/// This test verifies:
/// - NormalizeColumnCode(long) ends with _[0-9a-f]{12} and length <= 63
/// - two long codes with same prefix produce different column_code values
/// - EnsureSchema + append writes create physical columns and persist values
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterResources_LongCodes_TruncHash_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Post_WithLongResourceCodes_UsesHashedColumnCodes_AndCreatesPhysicalColumns()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        // Long codes with a shared prefix that would collide under naive truncation.
        var sharedPrefix = new string('a', 90);
        var code1 = sharedPrefix + "_tail_1";
        var code2 = sharedPrefix + "_tail_2";

        var col1 = OperationalRegisterNaming.NormalizeColumnCode(code1);
        var col2 = OperationalRegisterNaming.NormalizeColumnCode(code2);

        col1.Should().NotBe(col2, "hash suffix must prevent truncation collisions for long codes");
        col1.Length.Should().BeLessThanOrEqualTo(63);
        col2.Length.Should().BeLessThanOrEqualTo(63);
        col1.Should().MatchRegex("^[a-z0-9_]+$");
        col2.Should().MatchRegex("^[a-z0-9_]+$");
        col1.Should().MatchRegex("_[0-9a-f]{12}$");
        col2.Should().MatchRegex("_[0-9a-f]{12}$");

        var registerCode = "long_res_" + Guid.CreateVersion7().ToString("N");

        await SeedRegisterAndDocumentAsync(host, registerId, registerCode, documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition(code1, "R1", 1),
            new OperationalRegisterResourceDefinition(code2, "R2", 2)
        });

        var movements = new[]
        {
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    [col1] = 10m,
                    [col2] = 20m
                })
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                movements,
                affectedPeriods: null);
        }

        // Verify physical columns exist and values were persisted.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var regs = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var reg = await regs.GetByIdAsync(registerId, CancellationToken.None);
            reg.Should().NotBeNull();

            var table = OperationalRegisterNaming.MovementsTable(reg!.TableCode);

            var colCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                """
                SELECT COUNT(*)
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @RelName
                  AND column_name = ANY(@Cols);
                """,
                new { RelName = table, Cols = new[] { col1, col2 } },
                cancellationToken: CancellationToken.None));

            colCount.Should().Be(2);

            var row = await uow.Connection.QuerySingleAsync(new CommandDefinition(
                $"SELECT {col1} AS a, {col2} AS b FROM {table} WHERE document_id = @DocId LIMIT 1;",
                new { DocId = documentId },
                cancellationToken: CancellationToken.None));

            ((decimal)row.a).Should().Be(10m);
            ((decimal)row.b).Should().Be(20m);
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
            new OperationalRegisterUpsert(registerId, registerCode, "Long Resources"),
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
