using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.Accounts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_ChartOfAccounts_DimensionRules_Auditing_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateAccount_WithDimensionRules_AuditsSortedRules()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|coa-dimrules-create",
                        Email: "coa.dimrules@example.com",
                        DisplayName: "CoA DimRules User")));
            });

        Guid accountId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            var code = "AUD-DR-" + Guid.CreateVersion7().ToString("N")[..8];

            // Intentionally unsorted by ordinal.
            accountId = await svc.CreateAsync(
                new CreateAccountRequest(
                    Code: code,
                    Name: "Cash",
                    Type: AccountType.Asset,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                    IsActive: true,
                    DimensionRules:
                    [
                        new AccountDimensionRuleRequest("Counterparties", IsRequired: false, Ordinal: 20),
                        new AccountDimensionRuleRequest("Buildings", IsRequired: true, Ordinal: 10),
                    ]),
                CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var ev = await GetSingleEventAsync(
                reader,
                entityId: accountId,
                actionCode: AuditActionCodes.CoaAccountCreate);

            var change = ev.Changes.Single(c => c.FieldPath == "dimension_rules");
            change.OldValueJson.Should().BeNull("create must store dimension_rules as new-only change");
            change.NewValueJson.Should().NotBeNull();

            var rules = ParseRules(change.NewValueJson!);

            rules.Should().HaveCount(2);
            rules[0].Ordinal.Should().Be(10);
            rules[0].DimensionCode.Should().Be("Buildings");
            rules[0].IsRequired.Should().BeTrue();

            rules[1].Ordinal.Should().Be(20);
            rules[1].DimensionCode.Should().Be("Counterparties");
            rules[1].IsRequired.Should().BeFalse();
        }
    }

    [Fact]
    public async Task CreateAccount_WithNoDimensionRules_AuditsEmptyArray_NotNull()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|coa-dimrules-empty",
                        Email: null,
                        DisplayName: null)));
            });

        Guid accountId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            var code = "AUD-DR-" + Guid.CreateVersion7().ToString("N")[..8];

            accountId = await svc.CreateAsync(
                new CreateAccountRequest(Code: code, Name: "No dims", Type: AccountType.Asset),
                CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var ev = await GetSingleEventAsync(
                reader,
                entityId: accountId,
                actionCode: AuditActionCodes.CoaAccountCreate);

            var change = ev.Changes.Single(c => c.FieldPath == "dimension_rules");
            change.NewValueJson.Should().NotBeNull();
            change.NewValueJson!.Trim().Should().Be("[]", "dimension_rules is a stable contract: empty array, not null");
        }
    }

    [Fact]
    public async Task UpdateAccount_ReorderingSameDimensionRules_IsNoOp_AndNotAudited()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|coa-dimrules-reorder",
                        Email: null,
                        DisplayName: "CoA DimRules Admin")));
            });

        Guid accountId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            var code = "AUD-DR-" + Guid.CreateVersion7().ToString("N")[..8];
            accountId = await svc.CreateAsync(
                new CreateAccountRequest(
                    Code: code,
                    Name: "AR",
                    Type: AccountType.Asset,
                    DimensionRules:
                    [
                        new AccountDimensionRuleRequest("Buildings", IsRequired: true, Ordinal: 10),
                        new AccountDimensionRuleRequest("Counterparties", IsRequired: true, Ordinal: 20),
                    ]),
                CancellationToken.None);

            // Same rules, different order in request.
            await svc.UpdateAsync(
                new UpdateAccountRequest(
                    AccountId: accountId,
                    DimensionRules:
                    [
                        new AccountDimensionRuleRequest("Counterparties", IsRequired: true, Ordinal: 20),
                        new AccountDimensionRuleRequest("Buildings", IsRequired: true, Ordinal: 10),
                    ]),
                CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var updates = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ChartOfAccountsAccount,
                    EntityId: accountId,
                    ActionCode: AuditActionCodes.CoaAccountUpdate,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            updates.Should().BeEmpty("reordering in request must be a strict no-op and should not emit AuditLog event");
        }
    }

    [Fact]
    public async Task UpdateAccount_WhenDimensionRulesChange_AuditsOldAndNew()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|coa-dimrules-update",
                        Email: "coa.admin@example.com",
                        DisplayName: "CoA Admin")));
            });

        Guid accountId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            var code = "AUD-DR-" + Guid.CreateVersion7().ToString("N")[..8];
            accountId = await svc.CreateAsync(
                new CreateAccountRequest(
                    Code: code,
                    Name: "Cash",
                    Type: AccountType.Asset,
                    DimensionRules:
                    [
                        new AccountDimensionRuleRequest("Buildings", IsRequired: true, Ordinal: 10),
                        new AccountDimensionRuleRequest("Counterparties", IsRequired: false, Ordinal: 20),
                    ]),
                CancellationToken.None);

            // Change: flip IsRequired for counterparties and add Contracts.
            await svc.UpdateAsync(
                new UpdateAccountRequest(
                    AccountId: accountId,
                    DimensionRules:
                    [
                        new AccountDimensionRuleRequest("Buildings", IsRequired: true, Ordinal: 10),
                        new AccountDimensionRuleRequest("Counterparties", IsRequired: true, Ordinal: 20),
                        new AccountDimensionRuleRequest("Contracts", IsRequired: false, Ordinal: 30),
                    ]),
                CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var ev = await GetSingleEventAsync(
                reader,
                entityId: accountId,
                actionCode: AuditActionCodes.CoaAccountUpdate);

            var change = ev.Changes.Single(c => c.FieldPath == "dimension_rules");
            change.OldValueJson.Should().NotBeNull();
            change.NewValueJson.Should().NotBeNull();

            var oldRules = ParseRules(change.OldValueJson!);
            var newRules = ParseRules(change.NewValueJson!);

            oldRules.Should().HaveCount(2);
            oldRules[0].DimensionCode.Should().Be("Buildings");
            oldRules[1].DimensionCode.Should().Be("Counterparties");
            oldRules[1].IsRequired.Should().BeFalse();

            newRules.Should().HaveCount(3);
            newRules.Select(r => r.DimensionCode).Should().ContainInOrder("Buildings", "Counterparties", "Contracts");
            newRules.Single(r => r.DimensionCode == "Counterparties").IsRequired.Should().BeTrue();
        }
    }

    private static (int Ordinal, string DimensionCode, bool IsRequired)[] ParseRules(string json)
    {
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);

        return doc.RootElement
            .EnumerateArray()
            .Select(e =>
            {
                var ordinal = e.GetProperty("ordinal").GetInt32();
                var code = e.GetProperty("dimensionCode").GetString();
                var required = e.GetProperty("isRequired").GetBoolean();

                code.Should().NotBeNullOrWhiteSpace();
                return (ordinal, code!, required);
            })
            .ToArray();
    }

    private static async Task<AuditEvent> GetSingleEventAsync(
        IAuditEventReader reader,
        Guid entityId,
        string actionCode)
    {
        var events = await reader.QueryAsync(
            new AuditLogQuery(
                EntityKind: AuditEntityKind.ChartOfAccountsAccount,
                EntityId: entityId,
                ActionCode: actionCode,
                Limit: 50,
                Offset: 0),
            CancellationToken.None);

        events.Should().ContainSingle();
        return events.Single();
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}

