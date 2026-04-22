using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Core.AuditLog;
using NGB.Persistence.Accounts;
using NGB.Persistence.AuditLog;
using NGB.Runtime.Accounts;
using NGB.Runtime.Accounts.Exceptions;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_ChartOfAccounts_DimensionRules_And_Immutability_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly ActorIdentity Actor = new(
        AuthSubject: "test|user",
        Email: "test@example.com",
        DisplayName: "Test User",
        IsActive: true);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task UpdateAsync_WhenClientChangesOrdinals_TreatsAsExplicit_AndAuditsRules()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<ICurrentActorContext>(new FixedCurrentActorContext(Actor)));

        await using var scope = host.Services.CreateAsyncScope();

        var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var accountId = await coa.CreateAsync(
            new CreateAccountRequest(
                Code: "A200",
                Name: "Account",
                Type: AccountType.Asset,
                IsActive: true,
                DimensionRules: new[]
                {
                    new AccountDimensionRuleRequest(Ordinal: 10, DimensionCode: "building", IsRequired: true),
                    new AccountDimensionRuleRequest(Ordinal: 20, DimensionCode: "counterparty", IsRequired: false),
                    new AccountDimensionRuleRequest(Ordinal: 30, DimensionCode: "unit", IsRequired: false)
                }));

        await coa.UpdateAsync(
            new UpdateAccountRequest(
                AccountId: accountId,
                DimensionRules: new[]
                {
                    // Ordinals are explicit. Sending 1..3 is a real change.
                    new AccountDimensionRuleRequest(Ordinal: 1, DimensionCode: "building", IsRequired: true),
                    new AccountDimensionRuleRequest(Ordinal: 2, DimensionCode: "counterparty", IsRequired: false),
                    new AccountDimensionRuleRequest(Ordinal: 3, DimensionCode: "unit", IsRequired: false),
                    // Additional unrelated rule (must remain untouched).
                    new AccountDimensionRuleRequest(Ordinal: 40, DimensionCode: "contract", IsRequired: false)
                }));

        var item = await repo.GetAdminByIdAsync(accountId);
        item.Should().NotBeNull();
        item!.Account.DimensionRules.Select(x => x.Ordinal).Should().Equal(1, 2, 3, 40);

        var events = await reader.QueryAsync(new AuditLogQuery(EntityId: accountId, Limit: 100));
        var update = events.Single(x => x.ActionCode == AuditActionCodes.CoaAccountUpdate);
        var rulesChange = update.Changes.Single(x => x.FieldPath == "dimension_rules");

        var newRules = JsonSerializer.Deserialize<AuditDimensionRule[]>(rulesChange.NewValueJson!, JsonOptions);
        newRules.Should().NotBeNull();
        newRules!.Select(x => x.Ordinal).Should().Equal(1, 2, 3, 40);
    }

    [Fact]
    public async Task UpdateAsync_WhenClientOnlyReordersRules_DoesNotWriteAuditEvent()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<ICurrentActorContext>(new FixedCurrentActorContext(Actor)));

        await using var scope = host.Services.CreateAsyncScope();

        var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var accountId = await coa.CreateAsync(
            new CreateAccountRequest(
                Code: "A250",
                Name: "Account",
                Type: AccountType.Asset,
                IsActive: true,
                DimensionRules: new[]
                {
                    new AccountDimensionRuleRequest(Ordinal: 10, DimensionCode: "building", IsRequired: true),
                    new AccountDimensionRuleRequest(Ordinal: 20, DimensionCode: "counterparty", IsRequired: false),
                    new AccountDimensionRuleRequest(Ordinal: 30, DimensionCode: "unit", IsRequired: false)
                }));

        await coa.UpdateAsync(
            new UpdateAccountRequest(
                AccountId: accountId,
                // Reordered only.
                DimensionRules: new[]
                {
                    new AccountDimensionRuleRequest(Ordinal: 30, DimensionCode: "unit", IsRequired: false),
                    new AccountDimensionRuleRequest(Ordinal: 10, DimensionCode: "building", IsRequired: true),
                    new AccountDimensionRuleRequest(Ordinal: 20, DimensionCode: "counterparty", IsRequired: false)
                }));

        var events = await reader.QueryAsync(new AuditLogQuery(EntityId: accountId, Limit: 100));
        events.Should().NotContain(x => x.ActionCode == AuditActionCodes.CoaAccountUpdate);
    }

    [Fact]
    public async Task UpdateAsync_WhenAccountHasMovements_ChangingDimensionRulesThrows_AndDoesNotWriteAuditEvent()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<ICurrentActorContext>(new FixedCurrentActorContext(Actor)));

        await using var scope = host.Services.CreateAsyncScope();

        var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var accountId = await coa.CreateAsync(
            new CreateAccountRequest(
                Code: "A300",
                Name: "Account",
                Type: AccountType.Asset,
                IsActive: true,
                DimensionRules: new[]
                {
                    new AccountDimensionRuleRequest(Ordinal: 10, DimensionCode: "building", IsRequired: true),
                    new AccountDimensionRuleRequest(Ordinal: 20, DimensionCode: "counterparty", IsRequired: false),
                    new AccountDimensionRuleRequest(Ordinal: 30, DimensionCode: "unit", IsRequired: false)
                }));

        // Create at least one movement so immutability rules apply.
        await ReportingTestHelpers.SeedSimpleMovementAsync(
            scope.ServiceProvider,
            debitAccountCode: "A300",
            creditAccountCode: "A399",
            amount: 10m);

        var act = async () =>
        {
            await coa.UpdateAsync(
                new UpdateAccountRequest(
                    AccountId: accountId,
                    DimensionRules: new[]
                    {
                        // Actual behavior change (required flipped + dimension replacement)
                        new AccountDimensionRuleRequest(Ordinal: 10, DimensionCode: "building", IsRequired: false),
                        new AccountDimensionRuleRequest(Ordinal: 20, DimensionCode: "counterparty", IsRequired: false),
                        new AccountDimensionRuleRequest(Ordinal: 30, DimensionCode: "contract", IsRequired: false)
                    }));
        };

        var ex = await act.Should().ThrowAsync<AccountHasMovementsImmutabilityViolationException>();
        ex.Which.AssertNgbError(AccountHasMovementsImmutabilityViolationException.ErrorCodeConst, "accountId", "attemptedChanges");

        var events = await reader.QueryAsync(new AuditLogQuery(EntityId: accountId, Limit: 100));
        events.Should().NotContain(x => x.ActionCode == AuditActionCodes.CoaAccountUpdate);
    }

    private sealed record AuditDimensionRule(int Ordinal, string DimensionCode, bool IsRequired);

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
