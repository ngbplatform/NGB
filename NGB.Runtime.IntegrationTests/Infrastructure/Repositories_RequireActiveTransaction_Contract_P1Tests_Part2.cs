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
using NGB.Persistence.Writers;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P1: The platform enforces transaction boundaries for all write operations.
/// This file complements <see cref="Repositories_RequireActiveTransaction_Contract_P1Tests"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Repositories_RequireActiveTransaction_Contract_P1Tests_Part2(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TxnRequired = "This operation requires an active transaction.";

    [Fact]
    public async Task CatalogRepository_Create_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

        var catalog = new CatalogRecord
        {
            Id = Guid.CreateVersion7(),
            CatalogCode = "test_catalog",
            IsDeleted = false,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var act = () => repo.CreateAsync(catalog, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task CatalogRepository_Touch_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

        var act = () => repo.TouchAsync(Guid.CreateVersion7(), DateTime.UtcNow, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task DocumentRepository_Create_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var doc = new DocumentRecord
        {
            Id = Guid.CreateVersion7(),
            TypeCode = "test_document",
            Number = "T-1",
            DateUtc = DateTime.UtcNow,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        };

        var act = () => repo.CreateAsync(doc, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task DocumentRepository_UpdateStatus_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
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
    public async Task ChartOfAccountsRepository_Create_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

        var account = new Account(
            id: Guid.CreateVersion7(),
            code: "A100",
            name: "Test",
            type: AccountType.Asset,
            statementSection: StatementSection.Assets,
            negativeBalancePolicy: NegativeBalancePolicy.Allow,
            isContra: false);

        var act = () => repo.CreateAsync(account, isActive: true, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task ChartOfAccountsRepository_Update_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

        var account = new Account(
            id: Guid.CreateVersion7(),
            code: "A200",
            name: "Test",
            type: AccountType.Asset,
            statementSection: StatementSection.Assets,
            negativeBalancePolicy: NegativeBalancePolicy.Allow,
            isContra: false);

        var act = () => repo.UpdateAsync(account, isActive: true, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task ChartOfAccountsRepository_SetActive_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

        var act = () => repo.SetActiveAsync(Guid.CreateVersion7(), isActive: false, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task ChartOfAccountsRepository_SoftDelete_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

        var act = () => repo.MarkForDeletionAsync(Guid.CreateVersion7(), CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task AccountingTurnoverWriter_Write_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverWriter>();

        var turnover = new AccountingTurnover
        {
            Period = new DateOnly(2026, 1, 1),
            AccountId = Guid.CreateVersion7(),
            DebitAmount = 1m,
            CreditAmount = 0m
        };

        var act = () => writer.WriteAsync(new[] { turnover }, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task AccountingBalanceWriter_Save_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAccountingBalanceWriter>();

        var balance = new AccountingBalance
        {
            Period = new DateOnly(2026, 1, 1),
            AccountId = Guid.CreateVersion7(),
            OpeningBalance = 0m,
            ClosingBalance = 1m
        };

        var act = () => writer.SaveAsync(new[] { balance }, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }
}
