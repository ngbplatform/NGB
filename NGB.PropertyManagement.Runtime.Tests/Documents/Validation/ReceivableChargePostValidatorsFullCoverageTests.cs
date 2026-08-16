using FluentAssertions;
using Moq;
using NGB.Core.Catalogs;
using NGB.Core.Documents;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Policy;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class ReceivableChargePostValidatorsFullCoverageTests
{
    [Fact]
    public async Task Receivable_charge_validator_covers_every_lease_classification_amount_and_property_state()
    {
        var documentId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var chargeTypeId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var chargeSequence = readers.SetupSequence(x => x.ReadReceivableChargeHeadAsync(documentId, It.IsAny<CancellationToken>()));
        foreach (var amount in new[] { 10m, 10m, 10m, 10m, 10m, 10m, 10m, 0m, 10m, 10m, 10m, 10m })
        {
            chargeSequence.ReturnsAsync(new PmReceivableChargeHead(
                documentId, partyId, propertyId, leaseId, chargeTypeId, DateOnly.MinValue, amount, null));
        }

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var leaseDocuments = documents.SetupSequence(x => x.GetAsync(leaseId, It.IsAny<CancellationToken>()));
        leaseDocuments.ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(leaseId, "wrong", DocumentStatus.Posted))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.MarkedForDeletion));
        for (var i = 0; i < 9; i++)
            leaseDocuments.ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.Posted));

        var catalogs = new Mock<ICatalogRepository>(MockBehavior.Strict);
        catalogs.SetupSequence(x => x.GetAsync(chargeTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogRecord?)null)
            .ReturnsAsync(Catalog(chargeTypeId, "wrong"))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.ReceivableChargeType, deleted: true))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.ReceivableChargeType))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.ReceivableChargeType))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.ReceivableChargeType))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.ReceivableChargeType))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.ReceivableChargeType))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.ReceivableChargeType));
        readers.SetupSequence(x => x.ReadChargeTypeHeadAsync(chargeTypeId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException())
            .ReturnsAsync(new PmChargeTypeHead(chargeTypeId, "Rent", Guid.CreateVersion7()))
            .ReturnsAsync(new PmChargeTypeHead(chargeTypeId, "Rent", Guid.CreateVersion7()))
            .ReturnsAsync(new PmChargeTypeHead(chargeTypeId, "Rent", Guid.CreateVersion7()))
            .ReturnsAsync(new PmChargeTypeHead(chargeTypeId, "Rent", Guid.CreateVersion7()))
            .ReturnsAsync(new PmChargeTypeHead(chargeTypeId, "Rent", Guid.CreateVersion7()));
        readers.Setup(x => x.ReadLeaseHeadAsync(leaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmLeaseHead(leaseId, partyId, propertyId, DateOnly.MinValue, null));
        readers.SetupSequence(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PmPropertyHead?)null)
            .ReturnsAsync(Property(propertyId, deleted: true))
            .ReturnsAsync(Property(propertyId, kind: "Building"))
            .ReturnsAsync(Property(propertyId));
        var validator = new ReceivableChargePostValidator(
            readers.Object,
            documents.Object,
            catalogs.Object,
            ValidTenantReader(partyId).Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.ReceivableCharge);
        for (var i = 0; i < 11; i++)
            await AssertThrows(Validate);
        await Validate();
        return;

        Task Validate() => validator.ValidateBeforePostAsync(Document(documentId, PropertyManagementCodes.ReceivableCharge), default);
    }

    [Fact]
    public async Task Receivable_credit_memo_validator_covers_lease_amount_classification_and_property_states()
    {
        var documentId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var chargeTypeId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var memoSequence = readers.SetupSequence(x => x.ReadReceivableCreditMemoHeadAsync(documentId, It.IsAny<CancellationToken>()));
        var cases = new (decimal Amount, Guid? ChargeTypeId)[]
        {
            (10m, chargeTypeId), (10m, chargeTypeId), (10m, chargeTypeId),
            (0m, chargeTypeId), (10m, null), (10m, chargeTypeId),
            (10m, chargeTypeId), (10m, chargeTypeId), (10m, chargeTypeId), (10m, chargeTypeId)
        };
        foreach (var item in cases)
        {
            memoSequence.ReturnsAsync(new PmReceivableCreditMemoHead(
                documentId, partyId, propertyId, leaseId, item.ChargeTypeId, DateOnly.MinValue, item.Amount, null));
        }

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var leaseDocuments = documents.SetupSequence(x => x.GetAsync(leaseId, It.IsAny<CancellationToken>()));
        leaseDocuments.ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(leaseId, "wrong", DocumentStatus.Posted))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.MarkedForDeletion));
        for (var i = 0; i < 7; i++)
            leaseDocuments.ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.Posted));

        readers.SetupSequence(x => x.ReadChargeTypeHeadAsync(chargeTypeId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException())
            .ReturnsAsync(new PmChargeTypeHead(chargeTypeId, "Rent", Guid.CreateVersion7()))
            .ReturnsAsync(new PmChargeTypeHead(chargeTypeId, "Rent", Guid.CreateVersion7()))
            .ReturnsAsync(new PmChargeTypeHead(chargeTypeId, "Rent", Guid.CreateVersion7()))
            .ReturnsAsync(new PmChargeTypeHead(chargeTypeId, "Rent", Guid.CreateVersion7()));
        readers.Setup(x => x.ReadLeaseHeadAsync(leaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmLeaseHead(leaseId, partyId, propertyId, DateOnly.MinValue, null));
        readers.SetupSequence(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PmPropertyHead?)null)
            .ReturnsAsync(Property(propertyId, deleted: true))
            .ReturnsAsync(Property(propertyId, kind: "Building"))
            .ReturnsAsync(Property(propertyId));
        var validator = new ReceivableCreditMemoPostValidator(
            readers.Object,
            documents.Object,
            ValidTenantReader(partyId).Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.ReceivableCreditMemo);
        for (var i = 0; i < 9; i++)
            await AssertThrows(Validate);
        await Validate();
        return;

        Task Validate() => validator.ValidateBeforePostAsync(Document(documentId, PropertyManagementCodes.ReceivableCreditMemo), default);
    }

    private static Mock<IPropertyManagementPartyReader> ValidTenantReader(Guid partyId)
    {
        var parties = new Mock<IPropertyManagementPartyReader>(MockBehavior.Strict);
        parties.Setup(x => x.TryGetAsync(partyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyManagementParty(partyId, "Tenant", true, false, false));
        return parties;
    }

    private static CatalogRecord Catalog(Guid id, string code, bool deleted = false)
        => new()
        {
            Id = id,
            CatalogCode = code,
            IsDeleted = deleted,
            CreatedAtUtc = DateTime.UnixEpoch,
            UpdatedAtUtc = DateTime.UnixEpoch
        };

    private static PmPropertyHead Property(Guid id, string kind = "Unit", bool deleted = false)
        => new(id, kind, null, deleted);

    private static Task AssertThrows(Func<Task> action)
        => action.Should().ThrowAsync<Exception>();

    private static DocumentRecord Document(Guid id, string typeCode, DocumentStatus status = DocumentStatus.Draft)
        => new()
        {
            Id = id,
            TypeCode = typeCode,
            DateUtc = DateTime.UnixEpoch,
            Status = status
        };
}
