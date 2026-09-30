using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Catalogs;
using NGB.Core.Documents;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Documents.Validation;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class BasicPayloadValidatorsFullCoverageTests
{
    [Fact]
    public async Task Lease_based_charge_payload_validators_cover_reference_states_amount_and_update_fallbacks()
    {
        await VerifyLeaseBasedChargeAsync(isLateFee: true);
        await VerifyLeaseBasedChargeAsync(isLateFee: false);
    }

    [Fact]
    public async Task Maintenance_payload_validator_covers_subject_and_valid_create_and_partial_updates()
    {
        var documentId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var categoryId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPropertyHead(propertyId, "Unit", null, false));
        readers.Setup(x => x.ReadMaintenanceRequestHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmMaintenanceRequestHead(
                documentId, propertyId, partyId, categoryId, "Normal", "Current subject", null, DateOnly.MinValue));
        var catalogs = new Mock<ICatalogRepository>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetAsync(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(categoryId, PropertyManagementCodes.MaintenanceCategory));
        var parties = new Mock<IPropertyManagementPartyReader>(MockBehavior.Strict);
        parties.Setup(x => x.TryGetAsync(partyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyManagementParty(partyId, "Tenant", true, false, false));
        var validator = new MaintenanceRequestPayloadValidator(readers.Object, catalogs.Object, parties.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.MaintenanceRequest);
        await AssertInvalid<MaintenanceRequestValidationException>(() => Create(validator, Payload(
            ("property_id", propertyId), ("party_id", partyId), ("category_id", categoryId),
            ("priority", "Normal"), ("subject", " "))));
        await Create(validator, Payload(
            ("property_id", propertyId), ("party_id", partyId), ("category_id", categoryId),
            ("priority", "High"), ("subject", "Leak")));
        await Update(validator, documentId, Payload(("subject", "Updated")));
        await Update(validator, documentId, Payload(("priority", "Low")));
    }

    [Fact]
    public async Task Payable_charge_payload_validator_covers_amount_and_valid_create_and_partial_updates()
    {
        var documentId = Guid.CreateVersion7();
        var vendorId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var chargeTypeId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadPayableChargeHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPayableChargeHead(
                documentId, vendorId, propertyId, chargeTypeId, DateOnly.MinValue, 10m, null, null));
        readers.Setup(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPropertyHead(propertyId, "Unit", null, false));
        var catalogs = new Mock<ICatalogRepository>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetAsync(vendorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(vendorId, PropertyManagementCodes.Party));
        catalogs.Setup(x => x.GetAsync(chargeTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.PayableChargeType));
        var catalogService = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogService.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Party, vendorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogItemDto(
                vendorId,
                "Vendor",
                Payload(("is_vendor", true)),
                IsMarkedForDeletion: false,
                IsDeleted: false));
        var validator = new PayableChargePayloadValidator(readers.Object, catalogs.Object, catalogService.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.PayableCharge);
        await AssertInvalid<PayableChargeValidationException>(() => Create(validator, Payload(
            ("party_id", vendorId), ("property_id", propertyId), ("charge_type_id", chargeTypeId), ("amount", 0m))));
        await Create(validator, Payload(
            ("party_id", vendorId), ("property_id", propertyId), ("charge_type_id", chargeTypeId), ("amount", 10m)));
        await Update(validator, documentId, Payload(("amount", 20m)));
        await Update(validator, documentId, Payload(("party_id", vendorId)));
    }

    [Fact]
    public async Task Work_order_payload_validator_covers_assignee_null_override_fallback_and_business_rules()
    {
        var documentId = Guid.CreateVersion7();
        var requestId = Guid.CreateVersion7();
        var assigneeId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadWorkOrderHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmWorkOrderHead(documentId, requestId, assigneeId, null, null, "Owner"));
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents.Setup(x => x.GetAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(requestId, PropertyManagementCodes.MaintenanceRequest, DocumentStatus.Posted));
        var catalogs = new Mock<ICatalogRepository>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetAsync(assigneeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(assigneeId, PropertyManagementCodes.Party));
        var validator = new WorkOrderPayloadValidator(readers.Object, documents.Object, catalogs.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.WorkOrder);
        await AssertInvalid<WorkOrderValidationException>(() => Create(validator, Payload(
            ("request_id", requestId), ("cost_responsibility", "invalid"))));
        await Create(validator, Payload(
            ("request_id", requestId), ("assigned_party_id", assigneeId), ("cost_responsibility", "Owner")));
        await Create(validator, Payload(
            ("request_id", requestId), ("cost_responsibility", "Tenant")));
        await Update(validator, documentId, Payload(("cost_responsibility", "Company")));
        await Update(validator, documentId, Payload(("assigned_party_id", null), ("request_id", requestId)));
    }

    [Fact]
    public async Task Work_order_completion_payload_validator_covers_outcome_and_partial_update_fallbacks()
    {
        var documentId = Guid.CreateVersion7();
        var workOrderId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadWorkOrderCompletionHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmWorkOrderCompletionHead(
                documentId, workOrderId, DateOnly.MinValue, "Completed", null));
        readers.Setup(x => x.ExistsOtherPostedWorkOrderCompletionAsync(
                workOrderId,
                It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents.Setup(x => x.GetAsync(workOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(workOrderId, PropertyManagementCodes.WorkOrder, DocumentStatus.Posted));
        var validator = new WorkOrderCompletionPayloadValidator(readers.Object, documents.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.WorkOrderCompletion);
        await AssertInvalid<WorkOrderCompletionValidationException>(() => Create(validator, Payload(
            ("work_order_id", workOrderId), ("outcome", "invalid"))));
        await Create(validator, Payload(("work_order_id", workOrderId), ("outcome", "Completed")));
        await Update(validator, documentId, Payload(("outcome", "Cancelled")));
        await Update(validator, documentId, Payload(("work_order_id", workOrderId)));
    }

    private static async Task VerifyLeaseBasedChargeAsync(bool isLateFee)
    {
        var documentId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var leaseDocuments = documents.SetupSequence(x => x.GetAsync(leaseId, It.IsAny<CancellationToken>()));
        leaseDocuments.ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(leaseId, "wrong", DocumentStatus.Posted))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.MarkedForDeletion));
        for (var i = 0; i < 5; i++)
            leaseDocuments.ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.Posted));

        IDocumentDraftPayloadValidator validator;
        if (isLateFee)
        {
            readers.Setup(x => x.ReadLateFeeChargeHeadAsync(documentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmLateFeeChargeHead(
                    documentId, Guid.CreateVersion7(), Guid.CreateVersion7(), leaseId, DateOnly.MinValue, 10m, null));
            validator = new LateFeeChargePayloadValidator(readers.Object, documents.Object);
            validator.TypeCode.Should().Be(PropertyManagementCodes.LateFeeCharge);
        }
        else
        {
            readers.Setup(x => x.ReadReceivableChargeHeadAsync(documentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmReceivableChargeHead(
                    documentId, Guid.CreateVersion7(), Guid.CreateVersion7(), leaseId, Guid.CreateVersion7(), DateOnly.MinValue, 10m, null));
            validator = new ReceivableChargePayloadValidator(readers.Object, documents.Object);
            validator.TypeCode.Should().Be(PropertyManagementCodes.ReceivableCharge);
        }

        for (var i = 0; i < 3; i++)
            await AssertInvalid<Exception>(() => Create(validator, Payload(("lease_id", leaseId), ("amount", 10m))));
        await AssertInvalid<Exception>(() => Create(validator, Payload(("lease_id", leaseId), ("amount", 0m))));
        await Create(validator, Payload(("lease_id", leaseId), ("amount", 10m)));
        await AssertInvalid<Exception>(() => Update(validator, documentId, Payload(("amount", 0m))));
        await Update(validator, documentId, Payload(("amount", 20m)));
        await Update(validator, documentId, Payload(("lease_id", leaseId)));
    }

    private static RecordPayload Payload(params (string Key, object? Value)[] values)
        => new(values.ToDictionary(
            pair => pair.Key,
            pair => JsonSerializer.SerializeToElement(pair.Value),
            StringComparer.Ordinal));

    private static Task Create(IDocumentDraftPayloadValidator validator, RecordPayload payload)
        => validator.ValidateCreateDraftPayloadAsync(payload, EmptyParts, default);

    private static Task Update(IDocumentDraftPayloadValidator validator, Guid documentId, RecordPayload payload)
        => validator.ValidateUpdateDraftPayloadAsync(documentId, payload, EmptyParts, default);

    private static Task AssertInvalid<T>(Func<Task> action) where T : Exception
        => action.Should().ThrowAsync<T>();

    private static CatalogRecord Catalog(Guid id, string code)
        => new()
        {
            Id = id,
            CatalogCode = code,
            IsDeleted = false,
            CreatedAtUtc = DateTime.UnixEpoch,
            UpdatedAtUtc = DateTime.UnixEpoch
        };

    private static DocumentRecord Document(Guid id, string typeCode, DocumentStatus status)
        => new()
        {
            Id = id,
            TypeCode = typeCode,
            DateUtc = DateTime.UnixEpoch,
            Status = status
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> EmptyParts
        = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.Ordinal);
}
