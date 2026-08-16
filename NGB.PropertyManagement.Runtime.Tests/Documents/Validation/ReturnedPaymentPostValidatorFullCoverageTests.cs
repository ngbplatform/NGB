using FluentAssertions;
using Moq;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Dimensions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class ReturnedPaymentPostValidatorFullCoverageTests
{
    [Fact]
    public async Task Validator_covers_all_references_dates_register_availability_and_repost_adjustment()
    {
        var documentId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var originalPaymentId = Guid.CreateVersion7();
        var registerId = Guid.CreateVersion7();
        var dimensionSetId = Guid.CreateVersion7();
        var receivedOn = new DateOnly(2026, 2, 1);
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var returnedSequence = readers.SetupSequence(x => x.ReadReceivableReturnedPaymentHeadAsync(
            documentId,
            It.IsAny<CancellationToken>()));
        for (var i = 1; i <= 16; i++)
        {
            returnedSequence.ReturnsAsync(new PmReceivableReturnedPaymentHead(
                documentId,
                partyId,
                propertyId,
                leaseId,
                originalPaymentId,
                null,
                i == 11 ? receivedOn.AddDays(-1) : receivedOn.AddDays(1),
                i == 4 ? 0m : 10m,
                null));
        }

        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var leaseDocuments = documents.SetupSequence(x => x.GetAsync(leaseId, It.IsAny<CancellationToken>()));
        leaseDocuments.ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(leaseId, "wrong", DocumentStatus.Posted))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.MarkedForDeletion));
        for (var i = 0; i < 13; i++)
            leaseDocuments.ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.Posted));

        var properties = readers.SetupSequence(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()));
        properties.ReturnsAsync((PmPropertyHead?)null)
            .ReturnsAsync(new PmPropertyHead(propertyId, "Unit", null, true))
            .ReturnsAsync(new PmPropertyHead(propertyId, "Building", null, false));
        for (var i = 0; i < 9; i++)
            properties.ReturnsAsync(new PmPropertyHead(propertyId, "Unit", null, false));

        var originalDocuments = documents.SetupSequence(x => x.GetAsync(originalPaymentId, It.IsAny<CancellationToken>()));
        originalDocuments.ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(originalPaymentId, "wrong", DocumentStatus.Posted))
            .ReturnsAsync(Document(originalPaymentId, PropertyManagementCodes.ReceivablePayment, DocumentStatus.Draft));
        for (var i = 0; i < 6; i++)
            originalDocuments.ReturnsAsync(Document(originalPaymentId, PropertyManagementCodes.ReceivablePayment, DocumentStatus.Posted));

        readers.Setup(x => x.ReadReceivablePaymentHeadAsync(originalPaymentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivablePaymentHead(
                originalPaymentId, partyId, propertyId, leaseId, null, receivedOn, 20m, null));
        var parties = new Mock<IPropertyManagementPartyReader>(MockBehavior.Strict);
        parties.Setup(x => x.TryGetAsync(partyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyManagementParty(partyId, "Tenant", true, false, false));
        var policy = new PropertyManagementAccountingPolicy(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), registerId, Guid.CreateVersion7());
        var policies = new Mock<IPropertyManagementAccountingPolicyReader>(MockBehavior.Strict);
        policies.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        var register = new OperationalRegisterAdminItem(
            registerId,
            "pm.receivables_open_items",
            "pm.receivables_open_items",
            "pm_receivables_open_items",
            "Receivables open items",
            true,
            DateTime.UnixEpoch,
            DateTime.UnixEpoch);
        var registers = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        registers.SetupSequence(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterAdminItem?)null)
            .ReturnsAsync(register)
            .ReturnsAsync(register)
            .ReturnsAsync(register)
            .ReturnsAsync(register);
        var dimensionSets = new Mock<IDimensionSetService>(MockBehavior.Strict);
        dimensionSets.Setup(x => x.GetOrCreateIdAsync(It.IsAny<DimensionBag>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dimensionSetId);
        var netReader = new Mock<IOperationalRegisterResourceNetReader>(MockBehavior.Strict);
        netReader.SetupSequence(x => x.GetNetByDimensionSetAsync(
                registerId,
                dimensionSetId,
                "amount",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1m)
            .ReturnsAsync(-5m)
            .ReturnsAsync(-10m)
            .ReturnsAsync(-10m);
        var validator = new ReceivableReturnedPaymentPostValidator(
            readers.Object,
            documents.Object,
            policies.Object,
            parties.Object,
            registers.Object,
            netReader.Object,
            dimensionSets.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.ReceivableReturnedPayment);
        for (var i = 0; i < 11; i++)
            await ((Func<Task>)(() => Validate())).Should().ThrowAsync<Exception>();
        await Validate();
        await ((Func<Task>)(() => Validate())).Should().ThrowAsync<Exception>();
        await ((Func<Task>)(() => Validate())).Should().ThrowAsync<Exception>();
        await Validate();
        await Validate(DocumentStatus.Posted);

        dimensionSets.Verify(
            x => x.GetOrCreateIdAsync(
                It.Is<DimensionBag>(bag => bag.Count == 4),
                It.IsAny<CancellationToken>()),
            Times.Exactly(4));
        return;

        Task Validate(DocumentStatus status = DocumentStatus.Draft)
            => validator.ValidateBeforePostAsync(
                Document(documentId, PropertyManagementCodes.ReceivableReturnedPayment, status),
                default);
    }

    private static DocumentRecord Document(Guid id, string typeCode, DocumentStatus status)
        => new()
        {
            Id = id,
            TypeCode = typeCode,
            DateUtc = DateTime.UnixEpoch,
            Status = status
        };
}
