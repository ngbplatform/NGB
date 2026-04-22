using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivableReturnedPayment_Unpost_StaleCompletedLog_Conflicts_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivableReturnedPayment_Unpost_StaleCompletedLog_Conflicts_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

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

            var returnedPayment = await SeedDraftReturnedPaymentAsync(scope.ServiceProvider);
            (await documents.PostAsync(PropertyManagementCodes.ReceivableReturnedPayment, returnedPayment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

            await EnsureCompletedLogAsync(uow, "accounting_posting_state", new
            {
                document_id = returnedPayment.Id,
                operation = 2,
                started_at_utc = DateTime.UtcNow.AddMinutes(-1),
                completed_at_utc = DateTime.UtcNow,
                attempt_id = Guid.CreateVersion7()
            }, CancellationToken.None);

            var act = () => documents.UnpostAsync(PropertyManagementCodes.ReceivableReturnedPayment, returnedPayment.Id, CancellationToken.None);
            await act.Should().ThrowAsync<NgbInvariantViolationException>()
                .WithMessage("*inconsistent*");

            var current = await documents.GetByIdAsync(PropertyManagementCodes.ReceivableReturnedPayment, returnedPayment.Id, CancellationToken.None);
            current.Status.Should().Be(DocumentStatus.Posted);
        }
        finally
        {
            await factory.DisposeAsync();
            factory.Dispose();
        }
    }

    private static async Task<DocumentDto> SeedDraftReturnedPaymentAsync(IServiceProvider services)
    {
        var setup = services.GetRequiredService<IPropertyManagementSetupService>();
        var catalogs = services.GetRequiredService<ICatalogService>();
        var documents = services.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

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

        var property = await catalogs.CreateAsync(PropertyManagementCodes.Property, Payload(new
        {
            kind = "Unit",
            parent_property_id = building.Id,
            unit_no = "101"
        }), CancellationToken.None);

        var lease = await documents.CreateDraftAsync(PropertyManagementCodes.Lease, Payload(new
        {
            property_id = property.Id,
            start_on_utc = "2026-02-01",
            end_on_utc = "2026-02-28",
            rent_amount = "1000.00"
        }, LeaseParts.PrimaryTenant(party.Id)), CancellationToken.None);

        var payment = await documents.CreateDraftAsync(PropertyManagementCodes.ReceivablePayment, Payload(new
        {
            lease_id = lease.Id,
            received_on_utc = "2026-02-07",
            amount = "100.00"
        }), CancellationToken.None);
        (await documents.PostAsync(PropertyManagementCodes.ReceivablePayment, payment.Id, CancellationToken.None)).Status.Should().Be(DocumentStatus.Posted);

        return await documents.CreateDraftAsync(PropertyManagementCodes.ReceivableReturnedPayment, Payload(new
        {
            original_payment_id = payment.Id,
            returned_on_utc = "2026-02-08",
            amount = "25.00"
        }), CancellationToken.None);
    }

    private static RecordPayload Payload(object obj, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var el = JsonSerializer.SerializeToElement(obj);
        var dict = new Dictionary<string, JsonElement>();
        foreach (var p in el.EnumerateObject())
            dict[p.Name] = p.Value;
        return new RecordPayload(dict, parts);
    }

    private static async Task EnsureCompletedLogAsync(IUnitOfWork uow, string table, object row, CancellationToken ct)
    {
        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            await uow.EnsureConnectionOpenAsync(innerCt);

            var sql = $"""
INSERT INTO {table} (document_id, operation, attempt_id, started_at_utc, completed_at_utc)
VALUES (@document_id, @operation, @attempt_id, @started_at_utc, @completed_at_utc)
ON CONFLICT (document_id, operation) DO UPDATE
SET attempt_id = EXCLUDED.attempt_id,
    started_at_utc = EXCLUDED.started_at_utc,
    completed_at_utc = EXCLUDED.completed_at_utc;
""";

            var cmd = new CommandDefinition(sql, row, transaction: uow.Transaction, cancellationToken: innerCt);
            await uow.Connection.ExecuteAsync(cmd);
        }, ct);
    }
}
