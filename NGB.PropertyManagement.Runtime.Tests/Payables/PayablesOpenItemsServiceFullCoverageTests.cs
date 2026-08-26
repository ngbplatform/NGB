using FluentAssertions;
using Moq;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Payables;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Payables;

public sealed class PayablesOpenItemsServiceFullCoverageTests
{
    [Fact]
    public async Task Request_validation_rejects_missing_context_and_invalid_month_boundaries()
    {
        var fixture = new Fixture();
        await AssertInvalid(() => fixture.QueryAsync(partyId: Guid.Empty));
        await AssertInvalid(() => fixture.QueryAsync(propertyId: Guid.Empty));
        await AssertInvalid(() => fixture.QueryAsync(asOf: new DateOnly(2026, 1, 2)));
        await AssertInvalid(() => fixture.QueryAsync(to: new DateOnly(2026, 1, 2)));
        await AssertInvalid(() => fixture.QueryAsync(asOf: new DateOnly(2026, 2, 1), to: new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public async Task Empty_register_uses_current_fallback_month_and_returns_empty_totals()
    {
        var fixture = new Fixture();
        fixture.Readers.Setup(x => x.ReadFirstPayablesActivityMonthAsync(
                fixture.PartyId, fixture.PropertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateOnly?)null);

        var result = await fixture.QueryAsync();

        result.RegisterId.Should().Be(fixture.RegisterId);
        result.Charges.Should().BeEmpty();
        result.Credits.Should().BeEmpty();
        result.TotalOutstanding.Should().Be(0m);
        result.TotalCredit.Should().Be(0m);
    }

    [Fact]
    public async Task Register_rows_are_aggregated_in_database_enriched_and_sorted_with_orphans_skipped()
    {
        var fixture = new Fixture();
        var itemDimensionId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.PayableItem}");
        var chargeEarly = Guid.Parse("00000000-0000-0000-0000-000000000010");
        var chargeLow = Guid.Parse("00000000-0000-0000-0000-000000000020");
        var chargeHigh = Guid.Parse("00000000-0000-0000-0000-000000000030");
        var paymentEarly = Guid.Parse("00000000-0000-0000-0000-000000000040");
        var paymentLow = Guid.Parse("00000000-0000-0000-0000-000000000050");
        var memoHigh = Guid.Parse("00000000-0000-0000-0000-000000000060");
        var missingInfo = Guid.CreateVersion7();
        var missingChargeHead = Guid.CreateVersion7();
        var missingCreditHead = Guid.CreateVersion7();
        var chargeType = Guid.CreateVersion7();
        fixture.Movements.Setup(x => x.GetResourceNetsByDimensionAsync(
                fixture.RegisterId, It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<IReadOnlyList<DimensionValue>>(),
                itemDimensionId, "amount", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new(chargeEarly, 5m, "Charge early"),
                new(chargeLow, 10m, "Charge low"),
                new(chargeHigh, 20m, "Charge high"),
                new(paymentEarly, -3m, null),
                new(paymentLow, -4m, null),
                new(memoHigh, -6m, null),
                new(missingInfo, 7m, null),
                new(missingChargeHead, 8m, null),
                new(missingCreditHead, -9m, null)
            ]);
        fixture.Movements.Setup(x => x.GetMaxPeriodMonthAsync(
                fixture.RegisterId, It.IsAny<IReadOnlyList<DimensionValue>>(), null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DateOnly(2099, 12, 1));
        fixture.Readers.Setup(x => x.ReadDocumentInfosAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Info(chargeEarly, PropertyManagementCodes.PayableCharge),
                Info(chargeLow, PropertyManagementCodes.PayableCharge),
                Info(chargeHigh, PropertyManagementCodes.PayableCharge),
                Info(paymentEarly, PropertyManagementCodes.PayablePayment),
                Info(paymentLow, PropertyManagementCodes.PayablePayment),
                Info(memoHigh, PropertyManagementCodes.PayableCreditMemo),
                Info(missingChargeHead, PropertyManagementCodes.PayableCharge),
                Info(missingCreditHead, "unknown")
            ]);
        fixture.Readers.Setup(x => x.ReadPayableChargeHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Charge(chargeEarly, chargeType, new DateOnly(2026, 1, 1), 50m),
                Charge(chargeLow, chargeType, new DateOnly(2026, 2, 1), 100m),
                Charge(chargeHigh, Guid.CreateVersion7(), new DateOnly(2026, 2, 1), 200m)
            ]);
        fixture.Readers.Setup(x => x.ReadPayablePaymentHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Payment(paymentEarly, new DateOnly(2026, 1, 5), 30m),
                Payment(paymentLow, new DateOnly(2026, 2, 5), 40m)
            ]);
        fixture.Readers.Setup(x => x.ReadPayableCreditMemoHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Memo(memoHigh, new DateOnly(2026, 2, 5), 60m)]);
        fixture.Readers.Setup(x => x.ReadPayableChargeTypeHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PmPayableChargeTypeHead(chargeType, "Utilities", null)]);

        var result = await fixture.QueryAsync(
            asOf: new DateOnly(2026, 2, 1),
            to: new DateOnly(2026, 3, 1));

        result.Charges.Select(x => x.ChargeDocumentId).Should().Equal(chargeEarly, chargeLow, chargeHigh);
        result.Charges[0].OutstandingAmount.Should().Be(5m);
        result.Charges[1].ChargeTypeDisplay.Should().Be("Utilities");
        result.Charges[2].ChargeTypeDisplay.Should().BeNull();
        result.Credits.Select(x => x.CreditDocumentId).Should().Equal(paymentEarly, paymentLow, memoHigh);
        result.TotalOutstanding.Should().Be(35m);
        result.TotalCredit.Should().Be(13m);
        fixture.Movements.Verify(x => x.GetResourceNetsByDimensionAsync(
            fixture.RegisterId, new DateOnly(2026, 2, 1), new DateOnly(2026, 3, 1),
            It.IsAny<IReadOnlyList<DimensionValue>>(), itemDimensionId, "amount",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Aggregated_open_items_are_read_once()
    {
        var fixture = new Fixture();
        var itemDimensionId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.PayableItem}");
        var id = Guid.CreateVersion7();
        fixture.Movements.Setup(x => x.GetResourceNetsByDimensionAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<IReadOnlyList<DimensionValue>>(),
                itemDimensionId, "amount", It.IsAny<CancellationToken>()))
            .ReturnsAsync([new(id, 1m, null)]);
        fixture.Readers.Setup(x => x.ReadDocumentInfosAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Info(id, PropertyManagementCodes.PayableCharge)]);
        fixture.Readers.Setup(x => x.ReadPayableChargeHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Charge(id, Guid.CreateVersion7(), new DateOnly(2026, 1, 1), 1m)]);

        (await fixture.QueryAsync()).TotalOutstanding.Should().Be(1m);
        fixture.Movements.Verify(x => x.GetResourceNetsByDimensionAsync(
            It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<IReadOnlyList<DimensionValue>>(),
            itemDimensionId, "amount", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task AssertInvalid(Func<Task> action)
        => await action.Should().ThrowAsync<PayablesRequestValidationException>();

    private static PmDocumentInfo Info(Guid id, string type) => new(id, type, $"N-{id}");

    private static PmPayableChargeHead Charge(Guid id, Guid type, DateOnly due, decimal amount)
        => new(id, Guid.CreateVersion7(), Guid.CreateVersion7(), type, due, amount, "INV", "memo");

    private static PmPayablePaymentHead Payment(Guid id, DateOnly date, decimal amount)
        => new(id, Guid.CreateVersion7(), Guid.CreateVersion7(), null, date, amount, "memo");

    private static PmPayableCreditMemoHead Memo(Guid id, DateOnly date, decimal amount)
        => new(id, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), date, amount, "memo");

    private sealed class Fixture
    {
        public Fixture()
        {
            Policy.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PropertyManagementAccountingPolicy(
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), RegisterId));
            Readers.Setup(x => x.ReadFirstPayablesActivityMonthAsync(PartyId, PropertyId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DateOnly(2026, 1, 1));
            Readers.Setup(x => x.ReadDocumentInfosAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Readers.Setup(x => x.ReadPayableChargeHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Readers.Setup(x => x.ReadPayablePaymentHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Readers.Setup(x => x.ReadPayableCreditMemoHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Readers.Setup(x => x.ReadPayableChargeTypeHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Movements.Setup(x => x.GetMaxPeriodMonthAsync(
                    It.IsAny<Guid>(), It.IsAny<IReadOnlyList<DimensionValue>>(), null, null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateOnly?)null);
            Movements.Setup(x => x.GetResourceNetsByDimensionAsync(
                    It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<IReadOnlyList<DimensionValue>>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Sut = new PayablesOpenItemsService(Policy.Object, Movements.Object, Readers.Object, Uow.Object);
        }

        public Guid PartyId { get; } = Guid.CreateVersion7();
        public Guid PropertyId { get; } = Guid.CreateVersion7();
        public Guid RegisterId { get; } = Guid.CreateVersion7();
        public Mock<IPropertyManagementAccountingPolicyReader> Policy { get; } = new();
        public Mock<IOperationalRegisterMovementsQueryReader> Movements { get; } = new();
        public Mock<IPropertyManagementDocumentReaders> Readers { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public PayablesOpenItemsService Sut { get; }

        public Task<(Guid RegisterId, IReadOnlyList<PayablesOpenChargeItemDetailsDto> Charges, IReadOnlyList<PayablesOpenCreditItemDetailsDto> Credits, decimal TotalOutstanding, decimal TotalCredit)> QueryAsync(
            Guid? partyId = null,
            Guid? propertyId = null,
            DateOnly? asOf = null,
            DateOnly? to = null)
            => Sut.GetOpenItemsAsync(partyId ?? PartyId, propertyId ?? PropertyId, asOf, to);
    }
}
