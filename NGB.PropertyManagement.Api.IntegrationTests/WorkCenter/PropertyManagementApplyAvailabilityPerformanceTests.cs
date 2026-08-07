using FluentAssertions;
using Moq;
using NGB.Contracts.Metadata;
using NGB.Core.Dimensions;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Payables;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.PropertyManagement.Runtime.Receivables;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.WorkCenter;

public sealed class PropertyManagementApplyAvailabilityPerformanceTests
{
    [Theory]
    [InlineData(125.00, true)]
    [InlineData(0.00, false)]
    public async Task Receivable_charge_uses_one_set_based_net_query(decimal net, bool expectedAllowed)
    {
        var documentId = Guid.NewGuid();
        var registerId = Guid.NewGuid();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var policy = new Mock<IPropertyManagementAccountingPolicyReader>(MockBehavior.Strict);
        var netReader = new Mock<IOperationalRegisterResourceNetReader>(MockBehavior.Strict);
        readers.Setup(candidate => candidate.ReadReceivableChargeHeadAsync(
                documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmReceivableChargeHead(
                documentId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                new DateOnly(2026, 8, 7),
                125m,
                null));
        policy.Setup(candidate => candidate.GetRequiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountingPolicy(receivablesRegisterId: registerId));
        netReader.Setup(candidate => candidate.GetNetByDimensionsAsync(
                registerId,
                It.Is<IReadOnlyList<DimensionValue>>(dimensions => dimensions.Count == 4),
                "amount",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(net);
        var source = new ReceivablesApplyAvailabilitySource(
            readers.Object,
            new Mock<IDocumentRepository>(MockBehavior.Strict).Object,
            policy.Object,
            netReader.Object);

        var result = await source.EvaluateAsync(
            PropertyManagementCodes.ReceivableCharge,
            documentId,
            DocumentStatus.Posted,
            CancellationToken.None);

        result.IsAllowed.Should().Be(expectedAllowed);
        readers.VerifyAll();
        policy.VerifyAll();
        netReader.VerifyAll();
        netReader.Verify(candidate => candidate.GetNetByDimensionSetAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(125.00, true)]
    [InlineData(0.00, false)]
    public async Task Payable_charge_uses_one_set_based_net_query(decimal net, bool expectedAllowed)
    {
        var documentId = Guid.NewGuid();
        var registerId = Guid.NewGuid();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var policy = new Mock<IPropertyManagementAccountingPolicyReader>(MockBehavior.Strict);
        var netReader = new Mock<IOperationalRegisterResourceNetReader>(MockBehavior.Strict);
        readers.Setup(candidate => candidate.ReadPayableChargeHeadAsync(
                documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPayableChargeHead(
                documentId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                new DateOnly(2026, 8, 7),
                125m,
                null,
                null));
        policy.Setup(candidate => candidate.GetRequiredAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccountingPolicy(payablesRegisterId: registerId));
        netReader.Setup(candidate => candidate.GetNetByDimensionsAsync(
                registerId,
                It.Is<IReadOnlyList<DimensionValue>>(dimensions => dimensions.Count == 3),
                "amount",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(net);
        var source = new PayablesApplyAvailabilitySource(readers.Object, policy.Object, netReader.Object);

        var result = await source.EvaluateAsync(
            PropertyManagementCodes.PayableCharge,
            documentId,
            DocumentStatus.Posted,
            CancellationToken.None);

        result.IsAllowed.Should().Be(expectedAllowed);
        readers.VerifyAll();
        policy.VerifyAll();
        netReader.VerifyAll();
        netReader.Verify(candidate => candidate.GetNetByDimensionSetAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static PropertyManagementAccountingPolicy AccountingPolicy(
        Guid? receivablesRegisterId = null,
        Guid? payablesRegisterId = null)
        => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            receivablesRegisterId ?? Guid.NewGuid(),
            payablesRegisterId ?? Guid.NewGuid());
}
