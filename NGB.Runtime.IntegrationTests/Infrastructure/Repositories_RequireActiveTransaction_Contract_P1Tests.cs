using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Registers;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Documents;
using NGB.Persistence.Periods;
using NGB.Persistence.PostingState;
using NGB.Persistence.Writers;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Repositories_RequireActiveTransaction_Contract_P1Tests(PostgresTestFixture fixture)
{
    private const string TxnRequired = "This operation requires an active transaction.";

    [Fact]
    public async Task PostingLogRepository_TryBegin_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IPostingStateRepository>();

        var act = () => repo.TryBeginAsync(Guid.CreateVersion7(), PostingOperation.Post, DateTime.UtcNow, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task PostingLogRepository_MarkCompleted_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IPostingStateRepository>();

        var act = () => repo.MarkCompletedAsync(Guid.CreateVersion7(), PostingOperation.Post, DateTime.UtcNow, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task CatalogRepository_GetForUpdate_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

        var act = () => repo.GetForUpdateAsync(Guid.CreateVersion7(), CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task CatalogRepository_MarkDeleted_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

        var act = () => repo.MarkForDeletionAsync(Guid.CreateVersion7(), DateTime.UtcNow, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task DocumentRepository_GetForUpdate_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var act = () => repo.GetForUpdateAsync(Guid.CreateVersion7(), CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task ClosedPeriodRepository_MarkClosed_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IClosedPeriodRepository>();

        var act = () => repo.MarkClosedAsync(new DateOnly(2026, 1, 1), "test", DateTime.UtcNow, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task AccountingEntryWriter_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAccountingEntryWriter>();

        var debit = new Account(Guid.CreateVersion7(), "100", "Test debit", AccountType.Asset);
        var credit = new Account(Guid.CreateVersion7(), "200", "Test credit", AccountType.Liability);

        var entry = new AccountingEntry
        {
            DocumentId = Guid.CreateVersion7(),
            Period = DateTime.UtcNow,
            Debit = debit,
            Credit = credit,
            Amount = 1m,
            IsStorno = false
        };

        var act = () => writer.WriteAsync(new[] { entry }, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task AccountingTurnoverWriter_DeleteForPeriod_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverWriter>();

        var act = () => writer.DeleteForPeriodAsync(new DateOnly(2026, 1, 1), CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task AccountingBalanceWriter_DeleteForPeriod_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAccountingBalanceWriter>();

        var act = () => writer.DeleteForPeriodAsync(new DateOnly(2026, 1, 1), CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }
}
