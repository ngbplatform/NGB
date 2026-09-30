using FluentAssertions;
using Moq;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Policy;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class TenantPostValidatorsFullCoverageTests
{
    [Fact]
    public async Task Late_fee_validator_covers_lease_amount_tenant_and_property_boundaries()
    {
        var documentId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var chargeSequence = readers.SetupSequence(x => x.ReadLateFeeChargeHeadAsync(documentId, It.IsAny<CancellationToken>()));
        foreach (var amount in new[] { 10m, 10m, 10m, 0m, 10m, 10m, 10m, 10m })
        {
            chargeSequence.ReturnsAsync(new PmLateFeeChargeHead(
                documentId, partyId, propertyId, leaseId, DateOnly.MinValue, amount, null));
        }

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents.SetupSequence(x => x.GetAsync(leaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(leaseId, "wrong", DocumentStatus.Posted))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.MarkedForDeletion))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.Posted))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.Posted))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.Posted))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.Posted))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.Posted));
        readers.Setup(x => x.ReadLeaseHeadAsync(leaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Lease(leaseId, partyId, propertyId));
        readers.SetupSequence(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PmPropertyHead?)null)
            .ReturnsAsync(Property(propertyId, deleted: true))
            .ReturnsAsync(Property(propertyId, kind: "Building"))
            .ReturnsAsync(Property(propertyId));
        var parties = ValidTenantReader(partyId);
        var validator = new LateFeeChargePostValidator(readers.Object, documents.Object, parties.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.LateFeeCharge);
        for (var i = 0; i < 8; i++)
        {
            if (i == 7)
                await Validate();
            else
                await AssertThrows<Exception>(Validate);
        }
        return;

        Task Validate() => validator.ValidateBeforePostAsync(Document(documentId, PropertyManagementCodes.LateFeeCharge), default);
    }

    [Fact]
    public async Task Receivable_payment_validator_covers_lease_amount_property_and_optional_bank_states()
    {
        var documentId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var bankId = Guid.CreateVersion7();
        var amounts = new[] { 10m, 10m, 10m, 0m, 10m, 10m, 10m, 10m, 10m, 10m, 10m };
        var bankIds = new Guid?[] { bankId, bankId, bankId, bankId, bankId, bankId, bankId, bankId, bankId, null, bankId };
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var paymentSequence = readers.SetupSequence(x => x.ReadReceivablePaymentHeadAsync(documentId, It.IsAny<CancellationToken>()));
        for (var i = 0; i < amounts.Length; i++)
        {
            paymentSequence.ReturnsAsync(new PmReceivablePaymentHead(
                documentId, partyId, propertyId, leaseId, bankIds[i], DateOnly.MinValue, amounts[i], null));
        }

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var leaseDocumentSequence = documents.SetupSequence(x => x.GetAsync(leaseId, It.IsAny<CancellationToken>()));
        leaseDocumentSequence.ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(leaseId, "wrong", DocumentStatus.Posted))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.MarkedForDeletion));
        for (var i = 0; i < 8; i++)
            leaseDocumentSequence.ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.Posted));

        readers.Setup(x => x.ReadLeaseHeadAsync(leaseId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Lease(leaseId, partyId, propertyId));
        readers.SetupSequence(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PmPropertyHead?)null)
            .ReturnsAsync(Property(propertyId, deleted: true))
            .ReturnsAsync(Property(propertyId, kind: "Building"))
            .ReturnsAsync(Property(propertyId))
            .ReturnsAsync(Property(propertyId))
            .ReturnsAsync(Property(propertyId))
            .ReturnsAsync(Property(propertyId));
        var bankAccounts = new Mock<IPropertyManagementBankAccountReader>(MockBehavior.Strict);
        bankAccounts.SetupSequence(x => x.TryGetAsync(bankId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PropertyManagementBankAccount?)null)
            .ReturnsAsync(Bank(bankId, deleted: true))
            .ReturnsAsync(Bank(bankId));
        var validator = new ReceivablePaymentPostValidator(
            readers.Object,
            documents.Object,
            bankAccounts.Object,
            ValidTenantReader(partyId).Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.ReceivablePayment);
        for (var i = 0; i < 9; i++)
            await AssertThrows<Exception>(Validate);
        await Validate();
        await Validate();
        return;

        Task Validate() => validator.ValidateBeforePostAsync(Document(documentId, PropertyManagementCodes.ReceivablePayment), default);
    }

    [Fact]
    public async Task Rent_charge_validator_covers_lease_states_amount_and_every_period_boundary()
    {
        var documentId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var leaseStart = new DateOnly(2026, 1, 1);
        var leaseEnd = new DateOnly(2026, 12, 31);
        var rents = new[]
        {
            Rent(10m, leaseStart, leaseEnd), Rent(10m, leaseStart, leaseEnd), Rent(10m, leaseStart, leaseEnd),
            Rent(0m, leaseStart, leaseEnd),
            Rent(10m, new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1)),
            Rent(10m, new DateOnly(2025, 12, 31), leaseStart),
            Rent(10m, leaseStart, new DateOnly(2027, 1, 1)),
            Rent(10m, leaseStart, leaseEnd),
            Rent(10m, leaseStart, leaseEnd)
        };
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var rentSequence = readers.SetupSequence(x => x.ReadRentChargeHeadAsync(documentId, It.IsAny<CancellationToken>()));
        foreach (var rent in rents)
            rentSequence.ReturnsAsync(rent);

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var documentSequence = documents.SetupSequence(x => x.GetAsync(leaseId, It.IsAny<CancellationToken>()));
        documentSequence.ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(leaseId, "wrong", DocumentStatus.Posted))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.MarkedForDeletion));
        for (var i = 0; i < 6; i++)
            documentSequence.ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.Posted));

        var leaseSequence = readers.SetupSequence(x => x.ReadLeaseHeadAsync(leaseId, It.IsAny<CancellationToken>()));
        for (var i = 0; i < 5; i++)
            leaseSequence.ReturnsAsync(Lease(leaseId, partyId, propertyId, leaseStart, leaseEnd));
        leaseSequence.ReturnsAsync(Lease(leaseId, partyId, propertyId, leaseStart, null));
        var validator = new RentChargePostValidator(readers.Object, documents.Object, ValidTenantReader(partyId).Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.RentCharge);
        for (var i = 0; i < 7; i++)
            await AssertThrows<Exception>(Validate);
        await Validate();
        await Validate();
        return;

        PmRentChargeHead Rent(decimal amount, DateOnly from, DateOnly to) => new(
            documentId, leaseId, partyId, propertyId, from, to, to, amount, null);
        Task Validate() => validator.ValidateBeforePostAsync(Document(documentId, PropertyManagementCodes.RentCharge), default);
    }

    private static Mock<IPropertyManagementPartyReader> ValidTenantReader(Guid partyId)
    {
        var parties = new Mock<IPropertyManagementPartyReader>(MockBehavior.Strict);
        parties.Setup(x => x.TryGetAsync(partyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyManagementParty(partyId, "Tenant", true, false, false));
        return parties;
    }

    private static PmLeaseHead Lease(
        Guid leaseId,
        Guid partyId,
        Guid propertyId,
        DateOnly? start = null,
        DateOnly? end = null)
        => new(leaseId, partyId, propertyId, start ?? DateOnly.MinValue, end);

    private static PmPropertyHead Property(Guid id, string kind = "Unit", bool deleted = false)
        => new(id, kind, null, deleted);

    private static PropertyManagementBankAccount Bank(Guid id, bool deleted = false)
        => new(id, "Operating", Guid.CreateVersion7(), false, deleted);

    private static Task AssertThrows<T>(Func<Task> action)
        where T : Exception
        => action.Should().ThrowAsync<T>();

    private static DocumentRecord Document(Guid id, string typeCode, DocumentStatus status = DocumentStatus.Draft)
        => new()
        {
            Id = id,
            TypeCode = typeCode,
            DateUtc = DateTime.UnixEpoch,
            Status = status
        };
}
