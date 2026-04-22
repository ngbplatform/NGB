using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class AccountingEntryReader_AccountMetadataHydration_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetByDocumentAsync_Hydrates_StatementSection_FromDb_EvenIfNotDefaultForAccountType()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();

        // Asset account with non-default statement section (default for Asset is Assets).
        var debitId = await svc.CreateAsync(
            new CreateAccountRequest(
                Code: "A-100",
                Name: "Asset posted under Equity (test)",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Equity,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        var creditId = await svc.CreateAsync(
            new CreateAccountRequest(
                Code: "I-100",
                Name: "Income",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        var docId = Guid.CreateVersion7();

        await InsertRegisterRowAsync(
            Fixture.ConnectionString,
            documentId: docId,
            periodUtc: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            debitAccountId: debitId,
            creditAccountId: creditId,
            amount: 10m);

        var entries = await reader.GetByDocumentAsync(docId, CancellationToken.None);

        entries.Should().HaveCount(1);
        entries[0].Debit.StatementSection.Should().Be(StatementSection.Equity);
        entries[0].Credit.StatementSection.Should().Be(StatementSection.Income);
    }

    [Fact]
    public async Task GetByDocumentAsync_Hydrates_IsContra_AndNormalBalance_FromDb()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var reader = scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>();

        var cashId = await svc.CreateAsync(
            new CreateAccountRequest(
                Code: "50",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        // Contra-asset: same statement section as Assets but opposite normal balance (Credit).
        var contraId = await svc.CreateAsync(
            new CreateAccountRequest(
                Code: "50-CA",
                Name: "Contra Cash (test)",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: true,
                NegativeBalancePolicy: NegativeBalancePolicy.Forbid),
            CancellationToken.None);

        var docId = Guid.CreateVersion7();

        await InsertRegisterRowAsync(
            Fixture.ConnectionString,
            documentId: docId,
            periodUtc: new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            debitAccountId: cashId,
            creditAccountId: contraId,
            amount: 10m);

        var entries = await reader.GetByDocumentAsync(docId, CancellationToken.None);

        entries.Should().HaveCount(1);

        entries[0].Credit.IsContra.Should().BeTrue();
        entries[0].Credit.NormalBalance.Should().Be(NormalBalance.Credit);
        entries[0].Credit.NegativeBalancePolicy.Should().Be(NegativeBalancePolicy.Forbid);
    }

    private static async Task InsertRegisterRowAsync(
        string cs,
        Guid documentId,
        DateTime periodUtc,
        Guid debitAccountId,
        Guid creditAccountId,
        decimal amount)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        await using var cmd = new NpgsqlCommand("""
            INSERT INTO accounting_register_main
            (document_id, period, debit_account_id, credit_account_id, amount, is_storno)
            VALUES
            (@document_id, @period, @debit_account_id, @credit_account_id, @amount, FALSE);
            """, conn);

        cmd.Parameters.AddWithValue("document_id", documentId);
        cmd.Parameters.AddWithValue("period", periodUtc);
        cmd.Parameters.AddWithValue("debit_account_id", debitAccountId);
        cmd.Parameters.AddWithValue("credit_account_id", creditAccountId);
        cmd.Parameters.AddWithValue("amount", amount);

        await cmd.ExecuteNonQueryAsync(CancellationToken.None);
    }
}
