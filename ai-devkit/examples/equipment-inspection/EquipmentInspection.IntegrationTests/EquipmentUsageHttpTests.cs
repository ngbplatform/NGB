using System.Net;
using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Common;
using NGB.Contracts.Documents;
using NGB.Contracts.Effects;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.AuditLog;
using NGB.PostgreSql.AuditLog;
using NGB.Runtime.OperationalRegisters;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class EquipmentUsageHttpTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Setup_requires_an_explicit_database(string connection)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Setup.Program.Main(["ConnectionStrings:DefaultConnection=" + connection]));

        Assert.Equal("ConnectionStrings:DefaultConnection is required.", error.Message);
    }

    [Fact]
    public async Task Usage_post_replay_repost_and_unpost_preserve_append_only_history_and_totals()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Metered bench")));
        var draft = await PostAsync<DocumentDto>(admin, "/api/documents/" + EquipmentUsageDefinitions.DocumentCode, new RecordPayload());
        var path = "/api/documents/" + EquipmentUsageDefinitions.DocumentCode + "/" + draft.Id;
        var state = await StateAsync(admin, path);
        using var incomplete = await ExecutePostAsync(admin, path, state.DocumentVersion, "missing-usage");
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        Assert.Empty(await HistoryAsync(host, draft));

        using var updated = await admin.PutAsJsonAsync(path, Payload(("equipment_id", equipment.Id), ("hours", 6.25m)));
        await EnsureSuccessAsync(updated);
        state = await StateAsync(admin, path);
        using var denied = await ExecutePostAsync(reader, path, state.DocumentVersion, "denied-usage");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var postedResponse = await ExecutePostAsync(admin, path, state.DocumentVersion, "post-usage");
        var posted = await ReadActionAsync(postedResponse);
        Assert.Equal(DocumentStatus.Posted, posted.Document.Status);
        var original = Assert.Single(await HistoryAsync(host, draft));
        Assert.False(original.IsStorno);
        Assert.Equal(6.25m, original.Values["hours"]);
        await using (var setupScope = host.Services.CreateAsyncScope())
        {
            var defaults = ActivatorUtilities.CreateInstance<EquipmentDefaults>(setupScope.ServiceProvider);
            await defaults.ApplyAsync(CancellationToken.None);
            var registers = setupScope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
            var register = OperationalRegisterId.FromCode(EquipmentUsageDefinitions.RegisterCode);
            await Assert.ThrowsAsync<OperationalRegisterResourcesAppendOnlyViolationException>(() =>
                registers.ReplaceResourcesAsync(register, []));
            await Assert.ThrowsAsync<OperationalRegisterDimensionRulesAppendOnlyViolationException>(() =>
                registers.ReplaceDimensionRulesAsync(register, []));
        }
        Assert.Equal(original.MovementId, Assert.Single(await HistoryAsync(host, draft)).MovementId);
        var effects = await admin.GetFromJsonAsync<DocumentEffectsDto>(path + "/effects", Json);
        var effect = Assert.Single(effects!.OperationalRegisterMovements);
        Assert.Equal(equipment.Id, Assert.Single(effect.Dimensions).ValueId);
        Assert.Equal("Metered bench", Assert.Single(effect.Dimensions).Display);
        Assert.Equal(6.25m, Assert.Single(effect.Resources).Value);
        Assert.Empty(effects.AccountingEntries);
        Assert.Empty(effects.ReferenceRegisterWrites);

        using var replay = await ExecutePostAsync(admin, path, state.DocumentVersion, "post-usage");
        Assert.Equal(posted.ExecutionId, (await ReadActionAsync(replay)).ExecutionId);
        Assert.Single(await HistoryAsync(host, draft));

        using var repost = await ExecuteActionAsync(admin, path, "repost", posted.DocumentVersion, "repost-usage");
        var reposted = await ReadActionAsync(repost);
        var afterRepost = await HistoryAsync(host, draft);
        Assert.Equal(3, afterRepost.Count);
        Assert.Contains(afterRepost, row => row.MovementId == original.MovementId && !row.IsStorno);
        Assert.Equal(6.25m, Net(afterRepost));
        await AssertProjectionAsync(host, 6.25m);

        using var stale = await ExecuteActionAsync(admin, path, "unpost", posted.DocumentVersion, "stale-usage");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(afterRepost.Select(row => row.MovementId), (await HistoryAsync(host, draft)).Select(row => row.MovementId));
        using var unpost = await ExecuteActionAsync(admin, path, "unpost", reposted.DocumentVersion, "unpost-usage");
        var unposted = await ReadActionAsync(unpost);
        Assert.Equal(DocumentStatus.Draft, unposted.Document.Status);
        var history = await HistoryAsync(host, draft);
        Assert.True(history.Count > afterRepost.Count);
        Assert.Equal(afterRepost.Select(row => row.MovementId), history.Take(afterRepost.Count).Select(row => row.MovementId));
        Assert.Equal(0m, Net(history));
        await AssertProjectionAsync(host, 0m);
    }

    [Fact]
    public async Task Equipment_dimensions_keep_paged_turnovers_and_balances_separate()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var first = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "First bench")));
        var second = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Second bench")));
        foreach (var (equipment, hours) in new[] { (first.Id, 2.5m), (first.Id, 1.5m), (second.Id, 5m) })
        {
            var document = await PostAsync<DocumentDto>(admin, "/api/documents/" + EquipmentUsageDefinitions.DocumentCode,
                Payload(("equipment_id", equipment), ("hours", hours)));
            var path = "/api/documents/" + EquipmentUsageDefinitions.DocumentCode + "/" + document.Id;
            var state = await StateAsync(admin, path);
            using var posted = await ExecutePostAsync(admin, path, state.DocumentVersion, document.Id.ToString());
            await EnsureSuccessAsync(posted);
        }

        await using var scope = host.Services.CreateAsyncScope();
        var register = OperationalRegisterId.FromCode(EquipmentUsageDefinitions.RegisterCode);
        var maintenance = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminMaintenanceService>();
        Assert.Equal(1, await maintenance.FinalizeRegisterDirtyAsync(register));
        Assert.Equal(0, await maintenance.FinalizeRegisterDirtyAsync(register));
        var reads = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
        foreach (var read in new Func<OperationalRegisterMonthlyProjectionPageRequest, CancellationToken,
                     Task<OperationalRegisterMonthlyProjectionPage>>[] { reads.GetTurnoversPageAsync, reads.GetBalancesPageAsync })
        {
            var request = new OperationalRegisterMonthlyProjectionPageRequest(register,
                CurrentMonth.AddMonths(-1), CurrentMonth.AddMonths(1), PageSize: 1);
            var values = new Dictionary<Guid, decimal>();
            OperationalRegisterMonthlyProjectionPage page;
            do
            {
                page = await read(request, CancellationToken.None);
                var row = Assert.Single(page.Lines);
                var dimension = Assert.Single(row.Dimensions);
                Assert.Equal(EquipmentDefinitions.EquipmentDimensionId, dimension.DimensionId);
                Assert.True(values.TryAdd(dimension.ValueId, row.Values["hours"]));
                Assert.InRange(values.Count, 1, 2);
                request = request with { Cursor = page.NextCursor };
            } while (page.HasMore);

            Assert.Equal(4m, values[first.Id]);
            Assert.Equal(5m, values[second.Id]);
            var filtered = await read(request with
            {
                Cursor = null,
                Dimensions = [new DimensionValue(EquipmentDefinitions.EquipmentDimensionId, first.Id)]
            }, CancellationToken.None);
            Assert.Equal(4m, Assert.Single(filtered.Lines).Values["hours"]);
            Assert.False(filtered.HasMore);
        }
    }

    [Fact]
    public async Task Failure_after_register_writes_rolls_back_movements_status_and_version()
    {
        var failure = new AuditFailure();
        await using var host = new InspectionHost(configureServices: services =>
        {
            services.AddScoped<PostgresAuditEventWriter>();
            services.AddScoped<IAuditEventWriter>(provider =>
                new FailingAuditWriter(provider.GetRequiredService<PostgresAuditEventWriter>(),
                    provider.GetRequiredService<IOperationalRegisterReadService>(), failure));
        });
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Rollback bench")));
        var draft = await PostAsync<DocumentDto>(admin, "/api/documents/" + EquipmentUsageDefinitions.DocumentCode,
            Payload(("equipment_id", equipment.Id), ("hours", 3m)));
        var path = "/api/documents/" + EquipmentUsageDefinitions.DocumentCode + "/" + draft.Id;
        var before = await StateAsync(admin, path);
        using var response = await ExecutePostAsync(admin, path, before.DocumentVersion, "rollback-usage");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(failure.Triggered);
        Assert.True(failure.SawMovement);
        var after = await StateAsync(admin, path);
        Assert.Equal(DocumentStatus.Draft, after.Document.Status);
        Assert.Equal(before.DocumentVersion, after.DocumentVersion);
        Assert.Empty(await HistoryAsync(host, draft));
        await AssertNoEffectsAsync(admin, path);
    }

    private static async Task<DocumentEditorStateDto> StateAsync(HttpClient client, string path)
        => (await client.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;

    private static DateOnly CurrentMonth
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return new DateOnly(today.Year, today.Month, 1);
        }
    }

    private static decimal Net(IEnumerable<OperationalRegisterMovementQueryReadRow> rows)
        => rows.Sum(row => (row.IsStorno ? -1m : 1m) * row.Values["hours"]);

    private static async Task<List<OperationalRegisterMovementQueryReadRow>> HistoryAsync(InspectionHost host, DocumentDto document)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var reads = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
        var request = new OperationalRegisterMovementsPageRequest(OperationalRegisterId.FromCode(EquipmentUsageDefinitions.RegisterCode),
            CurrentMonth.AddMonths(-1), CurrentMonth.AddMonths(1), DocumentId: document.Id, PageSize: 1);
        var rows = new List<OperationalRegisterMovementQueryReadRow>();
        OperationalRegisterMovementsPage page;
        do
        {
            page = await reads.GetMovementsPageAsync(request);
            rows.AddRange(page.Lines);
            Assert.InRange(rows.Count, 0, 10);
            request = request with { Cursor = page.NextCursor };
        } while (page.HasMore);

        Assert.Equal(rows.Count, rows.Select(row => row.MovementId).Distinct().Count());
        return rows;
    }

    private static async Task AssertProjectionAsync(InspectionHost host, decimal expected)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var register = OperationalRegisterId.FromCode(EquipmentUsageDefinitions.RegisterCode);
        var maintenance = scope.ServiceProvider.GetRequiredService<IOperationalRegisterAdminMaintenanceService>();
        Assert.Equal(1, await maintenance.FinalizeRegisterDirtyAsync(register));
        var reads = scope.ServiceProvider.GetRequiredService<IOperationalRegisterReadService>();
        var page = await reads.GetTurnoversPageAsync(new OperationalRegisterMonthlyProjectionPageRequest(
            register, CurrentMonth.AddMonths(-1), CurrentMonth.AddMonths(1)));
        Assert.Equal(expected, page.Lines.Sum(row => row.Values["hours"]));
    }

    private sealed class AuditFailure
    {
        public bool Triggered { get; set; }
        public bool SawMovement { get; set; }
    }

    private sealed class FailingAuditWriter(
        IAuditEventWriter inner,
        IOperationalRegisterReadService reads,
        AuditFailure failure) : IAuditEventWriter
    {
        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            await inner.WriteAsync(auditEvent, ct);
            if (auditEvent.ActionCode == "document.post")
            {
                var page = await reads.GetMovementsPageAsync(new OperationalRegisterMovementsPageRequest(
                    OperationalRegisterId.FromCode(EquipmentUsageDefinitions.RegisterCode),
                    CurrentMonth.AddMonths(-1), CurrentMonth.AddMonths(1), DocumentId: auditEvent.EntityId), ct);
                failure.SawMovement = page.Lines.Count == 1;
                failure.Triggered = true;
                throw new InvalidOperationException("Injected audit failure after the document effects were written.");
            }
        }

        public async Task WriteBatchAsync(IReadOnlyList<AuditEvent> auditEvents, CancellationToken ct = default)
        {
            foreach (var auditEvent in auditEvents)
                await WriteAsync(auditEvent, ct);
        }
    }
}
