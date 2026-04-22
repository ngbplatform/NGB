using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.PostingState;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Posting;
using NGB.Contracts.Admin;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Admin;

[Collection(PmIntegrationCollection.Name)]
public sealed class ChartOfAccounts_Audit_Immutability_Concurrency_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public ChartOfAccounts_Audit_Immutability_Concurrency_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CrudActions_AreAudited_And_NoOps_DoNotWriteSecondEvents()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        var code = "AUD-" + UniqueTag();

        // Create
        var create = await client.PostAsJsonAsync(
            "/api/chart-of-accounts",
            new ChartOfAccountsUpsertRequestDto(Code: code, Name: "Bank", AccountType: "Asset", IsActive: true));

        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await create.Content.ReadFromJsonAsync<ChartOfAccountsAccountDto>(Json);
        created.Should().NotBeNull();
        created!.Code.Should().Be(code);
        created.AccountId.Should().NotBe(Guid.Empty);

        await AssertAuditEventExistsAsync(factory, created.AccountId, AuditActionCodes.CoaAccountCreate);

        // Update (real change)
        var update1 = await client.PutAsJsonAsync(
            $"/api/chart-of-accounts/{created.AccountId}",
            new ChartOfAccountsUpsertRequestDto(Code: code, Name: "Bank Updated", AccountType: "Asset", IsActive: true));

        update1.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertAuditEventCountAsync(factory, created.AccountId, AuditActionCodes.CoaAccountUpdate, expectedCount: 1);

        // Update (strict no-op) must NOT log again
        var update2 = await client.PutAsJsonAsync(
            $"/api/chart-of-accounts/{created.AccountId}",
            new ChartOfAccountsUpsertRequestDto(Code: code, Name: "Bank Updated", AccountType: "Asset", IsActive: true));

        update2.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertAuditEventCountAsync(factory, created.AccountId, AuditActionCodes.CoaAccountUpdate, expectedCount: 1);

        // SetActive (real change)
        var setActive1 = await client.PostAsync($"/api/chart-of-accounts/{created.AccountId}/set-active?isActive=false", content: null);
        setActive1.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AssertAuditEventCountAsync(factory, created.AccountId, AuditActionCodes.CoaAccountSetActive, expectedCount: 1);

        // SetActive (strict no-op) must NOT log again
        var setActive2 = await client.PostAsync($"/api/chart-of-accounts/{created.AccountId}/set-active?isActive=false", content: null);
        setActive2.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AssertAuditEventCountAsync(factory, created.AccountId, AuditActionCodes.CoaAccountSetActive, expectedCount: 1);

        // Mark for deletion (real change)
        var mark1 = await client.PostAsync($"/api/chart-of-accounts/{created.AccountId}/mark-for-deletion", content: null);
        mark1.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AssertAuditEventCountAsync(factory, created.AccountId, AuditActionCodes.CoaAccountMarkForDeletion, expectedCount: 1);

        // Mark for deletion (strict no-op) must NOT log again
        var mark2 = await client.PostAsync($"/api/chart-of-accounts/{created.AccountId}/mark-for-deletion", content: null);
        mark2.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AssertAuditEventCountAsync(factory, created.AccountId, AuditActionCodes.CoaAccountMarkForDeletion, expectedCount: 1);

        // Unmark for deletion (real change)
        var unmark1 = await client.PostAsync($"/api/chart-of-accounts/{created.AccountId}/unmark-for-deletion", content: null);
        unmark1.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AssertAuditEventCountAsync(factory, created.AccountId, AuditActionCodes.CoaAccountUnmarkForDeletion, expectedCount: 1);

        // Unmark for deletion (strict no-op)
        var unmark2 = await client.PostAsync($"/api/chart-of-accounts/{created.AccountId}/unmark-for-deletion", content: null);
        unmark2.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AssertAuditEventCountAsync(factory, created.AccountId, AuditActionCodes.CoaAccountUnmarkForDeletion, expectedCount: 1);
    }

    [Fact]
    public async Task WhenAccountHasMovements_ImmutableFieldChangesFail_DoNotPersist_AndDoNotWriteAuditEvents()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        var tag = UniqueTag();
        var cashCode = "CASH-" + tag;
        var revCode = "REV-" + tag;

        // Create accounts via UI.
        var cash = await CreateAccountAsync(client, code: cashCode, name: "Cash", accountType: "Asset");
        _ = await CreateAccountAsync(client, code: revCode, name: "Revenue", accountType: "Income");

        // Create a single movement via PostingEngine (this flips HasMovements=true by register reference).
        await PostOnceAsync(factory, cashCode, revCode, documentId: Guid.CreateVersion7(), periodUtc: new DateTime(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc), amount: 10m);

        var beforeUpdateEvents = await GetAuditEventCountAsync(factory, cash.AccountId, AuditActionCodes.CoaAccountUpdate);
        beforeUpdateEvents.Should().Be(0);

        // Attempt to change immutable fields via UI (Code + AccountType).
        var badUpdate = await client.PutAsJsonAsync(
            $"/api/chart-of-accounts/{cash.AccountId}",
            new ChartOfAccountsUpsertRequestDto(Code: cashCode + "-X", Name: "Cash", AccountType: "Liability", IsActive: true));

        await AssertProblemAsync(
            badUpdate,
            HttpStatusCode.Conflict,
            expectedErrorCode: "coa.account.has_movements.immutability_violation");

        // Must not persist.
        var after = await client.GetFromJsonAsync<ChartOfAccountsAccountDto>($"/api/chart-of-accounts/{cash.AccountId}", Json);
        after.Should().NotBeNull();
        after!.Code.Should().Be(cashCode);
        after.AccountType.Should().Be("Asset");

        // Must not write audit event.
        var afterUpdateEvents = await GetAuditEventCountAsync(factory, cash.AccountId, AuditActionCodes.CoaAccountUpdate);
        afterUpdateEvents.Should().Be(0);

        // Mark-for-deletion is also forbidden when there are movements.
        var beforeMarkEvents = await GetAuditEventCountAsync(factory, cash.AccountId, AuditActionCodes.CoaAccountMarkForDeletion);

        var badMark = await client.PostAsync($"/api/chart-of-accounts/{cash.AccountId}/mark-for-deletion", content: null);
        await AssertProblemAsync(
            badMark,
            HttpStatusCode.Conflict,
            expectedErrorCode: "coa.account.has_movements.cannot_delete");

        var afterMarkEvents = await GetAuditEventCountAsync(factory, cash.AccountId, AuditActionCodes.CoaAccountMarkForDeletion);
        afterMarkEvents.Should().Be(beforeMarkEvents);

        var after2 = await client.GetFromJsonAsync<ChartOfAccountsAccountDto>($"/api/chart-of-accounts/{cash.AccountId}", Json);
        after2.Should().NotBeNull();
        after2!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentCreateSameCode_OneSucceeds_AndAuditIsAtomic()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        // Use a wide window to avoid any clock/precision flakiness.
        var fromUtc = DateTime.UtcNow.AddHours(-1);

        var code = "AP-" + UniqueTag();

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<HttpResponseMessage> TryCreateAsync()
        {
            await start.Task;
            return await client.PostAsJsonAsync(
                "/api/chart-of-accounts",
                new ChartOfAccountsUpsertRequestDto(Code: code, Name: "Accounts Payable", AccountType: "Liability", IsActive: true));
        }

        var t1 = TryCreateAsync();
        var t2 = TryCreateAsync();
        start.SetResult();

        var results = await Task.WhenAll(t1, t2);

        results.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);

        var failed = results.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        await AssertProblemAsync(
            failed,
            HttpStatusCode.Conflict,
            expectedErrorCode: "ngb.conflict.unique_violation");

        // Only one NOT-deleted row must exist.
        var page = await client.GetFromJsonAsync<ChartOfAccountsPageDto>(
            $"/api/chart-of-accounts?includeDeleted=true&search={code}&offset=0&limit=50",
            Json);

        page.Should().NotBeNull();
        page!.Items.Count(x => x.Code == code && x.IsDeleted == false).Should().Be(1);

        // Audit is atomic: only one create event for code=61 must exist in this window.
        await using var scope = factory.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var events = await reader.QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.ChartOfAccountsAccount,
            ActionCode: AuditActionCodes.CoaAccountCreate,
            FromUtc: fromUtc,
            ToUtc: DateTime.UtcNow.AddMinutes(1),
            Limit: 200,
            Offset: 0));

        // MetadataJson formatting is not stable (whitespace / naming policy). Parse instead of string-contains.
        events.Count(e => string.Equals(TryReadMetadataCode(e.MetadataJson), code, StringComparison.Ordinal))
            .Should().Be(1);
    }

    private static string? TryReadMetadataCode(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (root.TryGetProperty("code", out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString();

            if (root.TryGetProperty("Code", out var p2) && p2.ValueKind == JsonValueKind.String)
                return p2.GetString();

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HttpClient CreateClient(PmApiFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            // PM API enables HTTPS redirection. Using https scheme avoids redirects in TestServer.
            BaseAddress = new Uri("https://localhost")
        });

    private static async Task<ChartOfAccountsAccountDto> CreateAccountAsync(HttpClient client, string code, string name, string accountType)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/chart-of-accounts",
            new ChartOfAccountsUpsertRequestDto(Code: code, Name: name, AccountType: accountType, IsActive: true));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.Content.ReadFromJsonAsync<ChartOfAccountsAccountDto>(Json);
        dto.Should().NotBeNull();
        return dto!;
    }

    private static string UniqueTag()
    {
        // IMPORTANT: Guid v7 starts with a timestamp. Taking only the first few hex chars is NOT unique
        // within a short window (~65s for the first 8 chars). Use the random tail.
        var n = Guid.CreateVersion7().ToString("N");
        return n[^12..];
    }

    private static async Task PostOnceAsync(PmApiFactory factory, string cashCode, string revenueCode, Guid documentId, DateTime periodUtc, decimal amount)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await engine.PostAsync(PostingOperation.Post, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            var cash = chart.Get(cashCode);
            var revenue = chart.Get(revenueCode);
            ctx.Post(documentId, periodUtc, cash, revenue, amount);
        }, manageTransaction: true, CancellationToken.None);
    }

    private static async Task AssertAuditEventExistsAsync(PmApiFactory factory, Guid entityId, string actionCode)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var events = await reader.QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.ChartOfAccountsAccount,
            EntityId: entityId,
            ActionCode: actionCode,
            Limit: 50,
            Offset: 0));

        events.Should().ContainSingle();
    }

    private static async Task<int> GetAuditEventCountAsync(PmApiFactory factory, Guid entityId, string actionCode)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var events = await reader.QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.ChartOfAccountsAccount,
            EntityId: entityId,
            ActionCode: actionCode,
            Limit: 200,
            Offset: 0));

        return events.Count;
    }

    private static async Task AssertAuditEventCountAsync(PmApiFactory factory, Guid entityId, string actionCode, int expectedCount)
    {
        var count = await GetAuditEventCountAsync(factory, entityId, actionCode);
        count.Should().Be(expectedCount);
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedErrorCode)
    {
        response.StatusCode.Should().Be(expectedStatus);

        var root = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        root.ValueKind.Should().Be(JsonValueKind.Object);

        root.TryGetProperty("status", out var statusProp).Should().BeTrue();
        statusProp.GetInt32().Should().Be((int)expectedStatus);

        root.GetProperty("error").TryGetProperty("code", out var codeProp).Should().BeTrue();
        codeProp.GetString().Should().Be(expectedErrorCode);

        root.TryGetProperty("traceId", out var traceProp).Should().BeTrue();
        traceProp.GetString().Should().NotBeNullOrWhiteSpace();
    }
}
