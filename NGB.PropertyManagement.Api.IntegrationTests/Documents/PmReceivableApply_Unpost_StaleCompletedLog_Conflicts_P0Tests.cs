using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivableApply_Unpost_StaleCompletedLog_Conflicts_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivableApply_Unpost_StaleCompletedLog_Conflicts_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UnpostAsync_WhenCompletedUnpostLogExistsButDocumentIsStillPosted_FailsFast_AndStatusRemainsPosted()
    {
        var factory = new PmApiFactory(_fixture);
        try
        {
            using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await using var scope = factory.Services.CreateAsyncScope();

            var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var seeded = await SeedDraftReceivableApplyAsync(scope.ServiceProvider, appliedAmount: "60.00");
            (await documents.PostAsync(PropertyManagementCodes.ReceivableApply, seeded.Apply.Id, CancellationToken.None))
                .Status.Should().Be(DocumentStatus.Posted);

            await EnsureCompletedLogAsync(
                uow,
                seeded.RegisterId,
                seeded.Apply.Id,
                (short)OperationalRegisterWriteOperation.Unpost,
                DateTime.UtcNow.AddMinutes(-1),
                DateTime.UtcNow,
                Guid.CreateVersion7(),
                CancellationToken.None);

            var act = () => documents.UnpostAsync(PropertyManagementCodes.ReceivableApply, seeded.Apply.Id, CancellationToken.None);
            await act.Should().ThrowAsync<NgbInvariantViolationException>()
                .WithMessage("*inconsistent*");

            var current = await documents.GetByIdAsync(PropertyManagementCodes.ReceivableApply, seeded.Apply.Id, CancellationToken.None);
            current.Status.Should().Be(DocumentStatus.Posted);
        }
        finally
        {
            await factory.DisposeAsync();
            factory.Dispose();
        }
    }

    private static async Task<(DocumentDto Apply, Guid RegisterId)> SeedDraftReceivableApplyAsync(IServiceProvider services, string appliedAmount)
    {
        var setup = services.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = services.GetRequiredService<ICatalogService>();
        var documents = services.GetRequiredService<IDocumentService>();

        var setupResult = await setup.EnsureDefaultsAsync(CancellationToken.None);

        var party = await catalogs.CreateAsync(PropertyManagementCodes.Party, Payload(new { display = "P" }), CancellationToken.None);
        var building = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Building",
            display = "A",
            address_line1 = "A",
            city = "Hoboken",
            state = "NJ",
            zip = "07030"
        }), CancellationToken.None);

        var unit = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "101"
        }), CancellationToken.None);

        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = unit.Id,
            start_on_utc = "2026-02-01",
            end_on_utc = "2026-02-28",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);
        lease = await documents.PostAsync(PropertyManagementCodes.Lease, lease.Id, CancellationToken.None);

        var chargeTypes = await catalogs.GetPageAsync(PropertyManagementCodes.ReceivableChargeType, new PageRequestDto(0, 50, null), CancellationToken.None);
        var rentType = chargeTypes.Items.Single(x => string.Equals(x.Display, "Utility", StringComparison.OrdinalIgnoreCase));

        var charge = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableCharge, Payload(new
        {
            lease_id = lease.Id,
            charge_type_id = rentType.Id,
            due_on_utc = "2026-02-05",
            amount = "100.00",
            memo = "Charge"
        }), CancellationToken.None);
        charge = await documents.PostAsync(PropertyManagementCodes.ReceivableCharge, charge.Id, CancellationToken.None);

        var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
        {
            lease_id = lease.Id,
            received_on_utc = "2026-02-07",
            amount = "100.00",
            memo = "Payment"
        }), CancellationToken.None);
        payment = await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None);

        var apply = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableApply, Payload(new
        {
            credit_document_id = payment.Id,
            charge_document_id = charge.Id,
            applied_on_utc = "2026-02-07",
            amount = appliedAmount,
            memo = "Apply"
        }), CancellationToken.None);

        return (apply, setupResult.ReceivablesOpenItemsOperationalRegisterId);
    }

    private static RecordPayload Payload(object obj, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in el.EnumerateObject())
            dict[p.Name] = p.Value;
        return new RecordPayload(dict, parts);
    }

    private static async Task EnsureCompletedLogAsync(
        IUnitOfWork uow,
        Guid registerId,
        Guid documentId,
        short operation,
        DateTime startedAtUtc,
        DateTime completedAtUtc,
        Guid attemptId,
        CancellationToken ct)
    {
        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            await uow.EnsureConnectionOpenAsync(innerCt);

            var sql = """
INSERT INTO operational_register_write_state (register_id, document_id, operation, attempt_id, started_at_utc, completed_at_utc)
VALUES (@register_id, @document_id, @operation, @attempt_id, @started_at_utc, @completed_at_utc)
ON CONFLICT (register_id, document_id, operation) DO UPDATE
SET attempt_id = EXCLUDED.attempt_id,
    started_at_utc = EXCLUDED.started_at_utc,
    completed_at_utc = EXCLUDED.completed_at_utc;
""";

            var cmd = new CommandDefinition(
                sql,
                new
                {
                    register_id = registerId,
                    document_id = documentId,
                    operation,
                    attempt_id = attemptId,
                    started_at_utc = startedAtUtc,
                    completed_at_utc = completedAtUtc
                },
                transaction: uow.Transaction,
                cancellationToken: innerCt);
            await uow.Connection.ExecuteAsync(cmd);
        }, ct);
    }
}
