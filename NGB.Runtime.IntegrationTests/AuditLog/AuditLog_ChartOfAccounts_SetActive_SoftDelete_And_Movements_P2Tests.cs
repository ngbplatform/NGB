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
public sealed class AuditLog_ChartOfAccounts_SetActive_SoftDelete_And_Movements_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SetActive_WritesOldAndNewValues_AndMetadata()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|coa-active-user",
                        Email: "coa.active@example.com",
                        DisplayName: "CoA Active User")));
            });

        Guid accountId;
        string code;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            code = "SA-" + Guid.CreateVersion7().ToString("N")[..8];
            accountId = await svc.CreateAsync(
                new CreateAccountRequest(Code: code, Name: "Temp", Type: AccountType.Asset, IsActive: true),
                CancellationToken.None);

            await svc.SetActiveAsync(accountId, isActive: false, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ChartOfAccountsAccount,
                    EntityId: accountId,
                    ActionCode: AuditActionCodes.CoaAccountSetActive,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            ev.Changes.Should().ContainSingle(c => c.FieldPath == "is_active");
            var change = ev.Changes.Single(c => c.FieldPath == "is_active");

            JsonBool(change.OldValueJson).Should().BeTrue();
            JsonBool(change.NewValueJson).Should().BeFalse();

            var meta = JsonDocument.Parse(ev.MetadataJson!).RootElement;
            meta.GetProperty("code").GetString().Should().Be(code);
            meta.GetProperty("name").GetString().Should().Be("Temp");
        }
    }

    [Fact]
    public async Task SoftDelete_ActiveAccount_WritesIsDeleted_AndAlsoDeactivates()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|coa-delete-user",
                        Email: null,
                        DisplayName: "CoA Delete User")));
            });

        Guid accountId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            var code = "DEL-" + Guid.CreateVersion7().ToString("N")[..8];
            accountId = await svc.CreateAsync(
                new CreateAccountRequest(Code: code, Name: "ToDelete", Type: AccountType.Asset, IsActive: true),
                CancellationToken.None);

            await svc.MarkForDeletionAsync(accountId, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ChartOfAccountsAccount,
                    EntityId: accountId,
                    ActionCode: AuditActionCodes.CoaAccountMarkForDeletion,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            ev.Changes.Should().Contain(c => c.FieldPath == "is_deleted");
            JsonBool(ev.Changes.Single(c => c.FieldPath == "is_deleted").OldValueJson).Should().BeFalse();
            JsonBool(ev.Changes.Single(c => c.FieldPath == "is_deleted").NewValueJson).Should().BeTrue();

            // If the account was active, SoftDelete() also deactivates it.
            ev.Changes.Should().Contain(c => c.FieldPath == "is_active");
            JsonBool(ev.Changes.Single(c => c.FieldPath == "is_active").OldValueJson).Should().BeTrue();
            JsonBool(ev.Changes.Single(c => c.FieldPath == "is_active").NewValueJson).Should().BeFalse();
        }
    }

    [Fact]
    public async Task SoftDelete_WhenHasMovements_Throws_AndDoesNotWriteAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|coa-move-del",
                        Email: null,
                        DisplayName: null)));
            });

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await ReportingTestHelpers.PostAsync(
            host,
            documentId: Guid.CreateVersion7(),
            dateUtc: ReportingTestHelpers.Day1Utc,
            debitCode: "50",
            creditCode: "90.1",
            amount: 10m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            var act = async () => await svc.MarkForDeletionAsync(cashId, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<AccountHasMovementsCannotDeleteException>();
            ex.Which.AssertNgbError(AccountHasMovementsCannotDeleteException.ErrorCodeConst, "accountId");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

            // Still not deleted.
            var admin = await repo.GetAdminByIdAsync(cashId, CancellationToken.None);
            admin.Should().NotBeNull();
            admin!.IsDeleted.Should().BeFalse();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ChartOfAccountsAccount,
                    EntityId: cashId,
                    ActionCode: AuditActionCodes.CoaAccountMarkForDeletion,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().BeEmpty("failed business operation must not be audited");
        }
    }

    [Fact]
    public async Task Update_Name_WhenHasMovements_IsAllowed_AndWritesOnlyNameChange()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|coa-move-name",
                        Email: null,
                        DisplayName: "Mover")));
            });

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await ReportingTestHelpers.PostAsync(
            host,
            documentId: Guid.CreateVersion7(),
            dateUtc: ReportingTestHelpers.Day1Utc,
            debitCode: "50",
            creditCode: "90.1",
            amount: 10m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            await svc.UpdateAsync(
                new UpdateAccountRequest(AccountId: cashId, Name: "Cash Updated"),
                CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ChartOfAccountsAccount,
                    EntityId: cashId,
                    ActionCode: AuditActionCodes.CoaAccountUpdate,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            ev.Changes.Should().ContainSingle(c => c.FieldPath == "name");
            JsonString(ev.Changes.Single(c => c.FieldPath == "name").OldValueJson).Should().Be("Cash");
            JsonString(ev.Changes.Single(c => c.FieldPath == "name").NewValueJson).Should().Be("Cash Updated");

            // Guardrail: ensure we did not accidentally log any immutable fields.
            ev.Changes.Select(c => c.FieldPath)
                .Should()
                .Equal(new[] { "name" }, "only Name change is allowed when the account has movements");
        }
    }

    [Fact]
    public async Task Update_ImmutableField_WhenHasMovements_Throws_AndDoesNotWriteAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|coa-move-code",
                        Email: null,
                        DisplayName: null)));
            });

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        await ReportingTestHelpers.PostAsync(
            host,
            documentId: Guid.CreateVersion7(),
            dateUtc: ReportingTestHelpers.Day1Utc,
            debitCode: "50",
            creditCode: "90.1",
            amount: 10m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            var act = async () => await svc.UpdateAsync(
                new UpdateAccountRequest(AccountId: cashId, Code: "50X"),
                CancellationToken.None);

            var ex = await act.Should().ThrowAsync<AccountHasMovementsImmutabilityViolationException>();
            ex.Which.AssertNgbError(AccountHasMovementsImmutabilityViolationException.ErrorCodeConst, "accountId", "attemptedChanges");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.ChartOfAccountsAccount,
                    EntityId: cashId,
                    ActionCode: AuditActionCodes.CoaAccountUpdate,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().BeEmpty("failed business operation must not be audited");
        }
    }

    private static string? JsonString(string? json)
    {
        if (json is null)
            return null;

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetString();
    }

    private static bool? JsonBool(string? json)
    {
        if (json is null)
            return null;

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetBoolean();
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
