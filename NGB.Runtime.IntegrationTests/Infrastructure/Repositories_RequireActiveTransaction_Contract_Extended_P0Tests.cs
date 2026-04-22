using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.Turnovers;
using NGB.Core.Catalogs;
using NGB.Core.Documents;
using NGB.Persistence.Accounts;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Writers;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Repositories_RequireActiveTransaction_Contract_Extended_P0Tests(PostgresTestFixture fixture)
{
    private const string TxnRequired = "This operation requires an active transaction.";

    [Fact]
    public async Task CatalogRepository_Create_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

        var record = new CatalogRecord
        {
            Id = Guid.CreateVersion7(),
            CatalogCode = "TEST",
            IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var act = () => repo.CreateAsync(record, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task CatalogRepository_Touch_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

        var act = () => repo.TouchAsync(Guid.CreateVersion7(), DateTime.UtcNow, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task DocumentRepository_Create_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = DateTime.UtcNow;
        var record = new DocumentRecord
        {
            Id = Guid.CreateVersion7(),
            TypeCode = "TEST",
            Number = "0001",
            DateUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        };

        var act = () => repo.CreateAsync(record, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task DocumentRepository_UpdateStatus_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var act = () => repo.UpdateStatusAsync(
            documentId: Guid.CreateVersion7(),
            status: DocumentStatus.Posted,
            updatedAtUtc: DateTime.UtcNow,
            postedAtUtc: DateTime.UtcNow,
            markedForDeletionAtUtc: null,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task DocumentRelationshipRepository_TryCreate_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipRepository>();

        var nowUtc = DateTime.UtcNow;
        var record = new DocumentRelationshipRecord
        {
            Id = Guid.CreateVersion7(),
            FromDocumentId = Guid.CreateVersion7(),
            ToDocumentId = Guid.CreateVersion7(),
            RelationshipCode = "based_on",
            RelationshipCodeNorm = "based_on",
            CreatedAtUtc = nowUtc
        };

        Func<Task> act = async () => _ = await repo.TryCreateAsync(record, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task DocumentRelationshipRepository_TryDelete_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipRepository>();

        Func<Task> act = async () => _ = await repo.TryDeleteAsync(Guid.CreateVersion7(), CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task ChartOfAccountsRepository_Create_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

        var account = new Account(Guid.CreateVersion7(), "100", "Test", AccountType.Asset);

        var act = () => repo.CreateAsync(account, isActive: true, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task ChartOfAccountsRepository_Update_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

        var account = new Account(Guid.CreateVersion7(), "100", "Test", AccountType.Asset);

        var act = () => repo.UpdateAsync(account, isActive: true, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task ChartOfAccountsRepository_SetActive_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

        var act = () => repo.SetActiveAsync(Guid.CreateVersion7(), isActive: true, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task ChartOfAccountsRepository_SoftDelete_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

        var act = () => repo.MarkForDeletionAsync(Guid.CreateVersion7(), CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task AccountingTurnoverWriter_Write_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverWriter>();

        var act = () => writer.WriteAsync(new[]
        {
            new AccountingTurnover
            {
                Period = new DateOnly(2026, 1, 1),
                AccountId = Guid.CreateVersion7(),
                DebitAmount = 1m,
                CreditAmount = 0m
            }
        }, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task AccountingBalanceWriter_Save_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAccountingBalanceWriter>();

        var act = () => writer.SaveAsync(new[]
        {
            new AccountingBalance
            {
                Period = new DateOnly(2026, 1, 1),
                AccountId = Guid.CreateVersion7(),
                OpeningBalance = 0m,
                ClosingBalance = 1m
            }
        }, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task OperationalRegisterRepository_Upsert_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();

        var act = () => repo.UpsertAsync(
            new OperationalRegisterUpsert(Guid.CreateVersion7(), "RR", "Rent Roll"),
            nowUtc: DateTime.UtcNow,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task OperationalRegisterDimensionRules_Replace_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterDimensionRuleRepository>();

        var act = () => repo.ReplaceAsync(
            registerId: Guid.CreateVersion7(),
            rules: new[]
            {
                new OperationalRegisterDimensionRule(Guid.CreateVersion7(), "Buildings", Ordinal: 10, IsRequired: true)
            },
            nowUtc: DateTime.UtcNow,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task OperationalRegisterFinalization_MarkDirty_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

        var act = () => repo.MarkDirtyAsync(
            registerId: Guid.CreateVersion7(),
            period: new DateOnly(2026, 1, 1),
            dirtySinceUtc: DateTime.UtcNow,
            nowUtc: DateTime.UtcNow,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task OperationalRegisterWriteLog_TryBegin_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteStateRepository>();

        var act = () => repo.TryBeginAsync(
            registerId: Guid.CreateVersion7(),
            documentId: Guid.CreateVersion7(),
            operation: OperationalRegisterWriteOperation.Post,
            startedAtUtc: DateTime.UtcNow,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }
}
