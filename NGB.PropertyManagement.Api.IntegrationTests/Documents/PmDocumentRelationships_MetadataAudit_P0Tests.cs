using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Documents;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmDocumentRelationships_MetadataAudit_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmDocumentRelationships_MetadataAudit_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Metadata_Uses_Only_Approved_Mirrored_Document_Refs()
    {
        using var factory = new PmApiFactory(_fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var expected = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [PropertyManagementCodes.MaintenanceRequest] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [PropertyManagementCodes.WorkOrder] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["request_id"] = "created_from"
            },
            [PropertyManagementCodes.WorkOrderCompletion] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["work_order_id"] = "created_from"
            },
            [PropertyManagementCodes.Lease] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [PropertyManagementCodes.RentCharge] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [PropertyManagementCodes.ReceivableCharge] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [PropertyManagementCodes.LateFeeCharge] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [PropertyManagementCodes.ReceivablePayment] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [PropertyManagementCodes.ReceivableReturnedPayment] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [PropertyManagementCodes.ReceivableCreditMemo] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [PropertyManagementCodes.ReceivableApply] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [PropertyManagementCodes.PayableCharge] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [PropertyManagementCodes.PayablePayment] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [PropertyManagementCodes.PayableCreditMemo] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            [PropertyManagementCodes.PayableApply] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        foreach (var pair in expected)
        {
            var meta = await documents.GetTypeMetadataAsync(pair.Key, CancellationToken.None);
            var actual = GetMirroredDocumentRefs(meta);
            actual.Should().BeEquivalentTo(pair.Value, options => options.WithoutStrictOrdering(), $"{pair.Key} mirrored relationship policy is fixed by audit");
        }
    }

    [Fact]
    public async Task Metadata_Keeps_Explicit_And_Context_Document_Refs_NonMirrored()
    {
        using var factory = new PmApiFactory(_fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await AssertFieldNotMirroredAsync(documents, PropertyManagementCodes.ReceivableApply, "credit_document_id");
        await AssertFieldNotMirroredAsync(documents, PropertyManagementCodes.ReceivableApply, "charge_document_id");
        await AssertFieldNotMirroredAsync(documents, PropertyManagementCodes.PayableApply, "credit_document_id");
        await AssertFieldNotMirroredAsync(documents, PropertyManagementCodes.PayableApply, "charge_document_id");
        await AssertFieldNotMirroredAsync(documents, PropertyManagementCodes.ReceivableReturnedPayment, "original_payment_id");
        await AssertFieldNotMirroredAsync(documents, PropertyManagementCodes.RentCharge, "lease_id");
        await AssertFieldNotMirroredAsync(documents, PropertyManagementCodes.ReceivableCharge, "lease_id");
        await AssertFieldNotMirroredAsync(documents, PropertyManagementCodes.LateFeeCharge, "lease_id");
        await AssertFieldNotMirroredAsync(documents, PropertyManagementCodes.ReceivablePayment, "lease_id");
        await AssertFieldNotMirroredAsync(documents, PropertyManagementCodes.ReceivableCreditMemo, "lease_id");
    }

    private static async Task AssertFieldNotMirroredAsync(IDocumentService documents, string typeCode, string fieldKey)
    {
        var meta = await documents.GetTypeMetadataAsync(typeCode, CancellationToken.None);
        var field = meta.Form!.Sections
            .SelectMany(s => s.Rows)
            .SelectMany(r => r.Fields)
            .Single(f => string.Equals(f.Key, fieldKey, StringComparison.OrdinalIgnoreCase));

        field.Lookup.Should().BeOfType<DocumentLookupSourceDto>($"{typeCode}.{fieldKey} is a document reference field covered by this audit");
        field.MirroredRelationship.Should().BeNull($"{typeCode}.{fieldKey} is intentionally not mirrored; it is either explicit runtime flow logic or pure context");
    }

    private static Dictionary<string, string> GetMirroredDocumentRefs(DocumentTypeMetadataDto meta)
        => meta.Form!.Sections
            .SelectMany(s => s.Rows)
            .SelectMany(r => r.Fields)
            .Where(f => f.Lookup is DocumentLookupSourceDto && f.MirroredRelationship is not null)
            .ToDictionary(
                f => f.Key,
                f => f.MirroredRelationship!.RelationshipCode,
                StringComparer.OrdinalIgnoreCase);
}
