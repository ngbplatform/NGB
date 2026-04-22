using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.Registers;
using NGB.Accounting.Turnovers;
using NGB.Persistence.Writers;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Repositories_RequireActiveTransaction_Contract_AccountingWriters_P0Tests(PostgresTestFixture fixture)
{
    private const string TxnRequired = "This operation requires an active transaction.";

    [Fact]
    public async Task AccountingEntryWriter_Write_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAccountingEntryWriter>();

        var debit = new Account(Guid.CreateVersion7(), "it_debit", "IT Debit", AccountType.Asset);
        var credit = new Account(Guid.CreateVersion7(), "it_credit", "IT Credit", AccountType.Liability);

        var entry = new AccountingEntry
        {
            DocumentId = Guid.CreateVersion7(),
            Period = DateTime.UtcNow,
            Debit = debit,
            Credit = credit,
            Amount = 1m,
            DebitDimensionSetId = Guid.Empty,
            CreditDimensionSetId = Guid.Empty
        };

        Func<Task> act = () => writer.WriteAsync(new[] { entry }, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task AccountingTurnoverWriter_Methods_WithoutTransaction_Throw()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAccountingTurnoverWriter>();

        var t = new AccountingTurnover
        {
            Period = new DateOnly(2025, 1, 1),
            AccountId = Guid.CreateVersion7(),
            DimensionSetId = Guid.Empty,
            DebitAmount = 1m,
            CreditAmount = 0m
        };

        Func<Task> actDelete = () => writer.DeleteForPeriodAsync(new DateOnly(2025, 1, 1), CancellationToken.None);
        Func<Task> actWrite = () => writer.WriteAsync(new[] { t }, CancellationToken.None);

        await actDelete.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
        await actWrite.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task AccountingBalanceWriter_Methods_WithoutTransaction_Throw()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IAccountingBalanceWriter>();

        var b = new AccountingBalance
        {
            Period = new DateOnly(2025, 1, 1),
            AccountId = Guid.CreateVersion7(),
            DimensionSetId = Guid.Empty,
            OpeningBalance = 0m,
            ClosingBalance = 1m
        };

        Func<Task> actDelete = () => writer.DeleteForPeriodAsync(new DateOnly(2025, 1, 1), CancellationToken.None);
        Func<Task> actSave = () => writer.SaveAsync(new[] { b }, CancellationToken.None);

        await actDelete.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
        await actSave.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }
}
