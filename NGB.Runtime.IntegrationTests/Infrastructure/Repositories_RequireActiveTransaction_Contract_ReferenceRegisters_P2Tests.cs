using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Repositories_RequireActiveTransaction_Contract_ReferenceRegisters_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ReferenceRegisterRepository_Writes_RequireActiveTransaction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();

        var act = async () =>
        {
            await repo.UpsertAsync(
                new ReferenceRegisterUpsert(
                    RegisterId: Guid.CreateVersion7(),
                    Code: "RR_TXN",
                    Name: "RR Txn",
                    Periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                    RecordMode: ReferenceRegisterRecordMode.Independent),
                nowUtc: DateTime.UtcNow,
                ct: CancellationToken.None);
        };

        await act.Should()
            .ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*active transaction*");
    }

    [Fact]
    public async Task ReferenceRegisterFieldRepository_Replace_RequiresActiveTransaction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterFieldRepository>();

        var act = async () =>
        {
            await repo.ReplaceAsync(
                registerId: Guid.CreateVersion7(),
                fields:
                [
                    new ReferenceRegisterFieldDefinition(
                        Code: "Field1",
                        Name: "Field 1",
                        Ordinal: 1,
                        ColumnType: ColumnType.String,
                        IsNullable: true)
                ],
                nowUtc: DateTime.UtcNow,
                ct: CancellationToken.None);
        };

        await act.Should()
            .ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*active transaction*");
    }

    [Fact]
    public async Task ReferenceRegisterDimensionRuleRepository_Replace_RequiresActiveTransaction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterDimensionRuleRepository>();

        var act = async () =>
        {
            await repo.ReplaceAsync(
                registerId: Guid.CreateVersion7(),
                rules: [new ReferenceRegisterDimensionRule(Guid.CreateVersion7(), "", 100, true)],
                nowUtc: DateTime.UtcNow,
                ct: CancellationToken.None);
        };

        await act.Should()
            .ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*active transaction*");
    }

    [Fact]
    public async Task ReferenceRegisterWriteLogRepository_Writes_RequireActiveTransaction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterWriteStateRepository>();

        var act = async () =>
        {
            await repo.TryBeginAsync(
                registerId: Guid.CreateVersion7(),
                documentId: Guid.CreateVersion7(),
                operation: ReferenceRegisterWriteOperation.Post,
                startedAtUtc: DateTime.UtcNow,
                ct: CancellationToken.None);
        };

        await act.Should()
            .ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*active transaction*");
    }

    [Fact]
    public async Task ReferenceRegisterIndependentWriteLogRepository_Writes_RequireActiveTransaction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteStateRepository>();

        var act = async () =>
        {
            await repo.TryBeginAsync(
                registerId: Guid.CreateVersion7(),
                commandId: Guid.CreateVersion7(),
                operation: ReferenceRegisterIndependentWriteOperation.Upsert,
                startedAtUtc: DateTime.UtcNow,
                ct: CancellationToken.None);
        };

        await act.Should()
            .ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*active transaction*");
    }

    [Fact]
    public async Task ReferenceRegisterRecordsStore_Writes_RequireActiveTransaction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

        var actAppend = async () =>
        {
            await store.AppendAsync(
                registerId: Guid.CreateVersion7(),
                records:
                [
                    new ReferenceRegisterRecordWrite(
                        DimensionSetId: Guid.Empty,
                        PeriodUtc: null,
                        RecorderDocumentId: null,
                        Values: new Dictionary<string, object?>())
                ],
                ct: CancellationToken.None);
        };

        await actAppend.Should()
            .ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*active transaction*");
    }

    [Fact]
    public async Task ReferenceRegisterRecorderTombstoneWriter_Writes_RequireActiveTransaction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();

        var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
        var writer = store as IReferenceRegisterRecorderTombstoneWriter;
        writer.Should().NotBeNull("the records store implements recorder tombstone writes");

        var act = async () =>
        {
            await writer!.AppendTombstonesForRecorderAsync(
                registerId: Guid.CreateVersion7(),
                recorderDocumentId: Guid.CreateVersion7(),
                keepDimensionSetIds: null,
                ct: CancellationToken.None);
        };

        await act.Should()
            .ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*active transaction*");
    }
}
