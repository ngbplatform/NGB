using FluentAssertions;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Runtime.Payables;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Allocation;

public sealed class FifoAllocatorsFullCoverageTests
{
    private static readonly DateOnly Day = new(2026, 8, 16);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Payables_allocator_rejects_non_positive_limit(int limit)
        => ((Action)(() => PayablesFifoAllocator.Allocate([], [], limit)))
            .Should().Throw<NgbArgumentOutOfRangeException>();

    [Fact]
    public void Payables_allocator_filters_non_positive_items_and_handles_empty_plan()
    {
        var plan = PayablesFifoAllocator.Allocate(
            [PayableCharge(Guid.CreateVersion7(), Day, 0m), PayableCharge(Guid.CreateVersion7(), Day, -1m)],
            [PayableCredit(Guid.CreateVersion7(), Day, 0m), PayableCredit(Guid.CreateVersion7(), Day, -1m)],
            limit: null);

        plan.Lines.Should().BeEmpty();
        plan.RemainingOutstanding.Should().Be(0m);
        plan.RemainingCredit.Should().Be(0m);
        plan.LimitReached.Should().BeFalse();
    }

    [Fact]
    public void Payables_allocator_orders_fifo_skips_paid_charges_and_tracks_all_balances()
    {
        var earlyCharge = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var sameDayEarlierId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var lateCharge = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var firstCredit = Guid.Parse("00000000-0000-0000-0000-000000000011");
        var secondCredit = Guid.Parse("00000000-0000-0000-0000-000000000012");

        var plan = PayablesFifoAllocator.Allocate(
            [
                PayableCharge(lateCharge, Day.AddDays(1), 4m),
                PayableCharge(earlyCharge, Day, 3m),
                PayableCharge(sameDayEarlierId, Day, 2m)
            ],
            [
                PayableCredit(secondCredit, Day.AddDays(1), 5m),
                PayableCredit(firstCredit, Day, 5m)
            ],
            limit: null);

        plan.Lines.Should().HaveCount(3);
        plan.Lines.Select(x => (x.CreditDocumentId, x.ChargeDocumentId, x.Amount)).Should().Equal(
            (firstCredit, sameDayEarlierId, 2m),
            (firstCredit, earlyCharge, 3m),
            (secondCredit, lateCharge, 4m));
        plan.Lines.Last().Amount.Should().BeGreaterThan(0m);
        plan.RemainingOutstanding.Should().Be(0m);
        plan.RemainingCredit.Should().Be(1m);
        plan.LimitReached.Should().BeFalse();
    }

    [Fact]
    public void Payables_allocator_stops_at_positive_limit()
    {
        var plan = PayablesFifoAllocator.Allocate(
            [PayableCharge(Guid.CreateVersion7(), Day, 2m), PayableCharge(Guid.CreateVersion7(), Day.AddDays(1), 2m)],
            [PayableCredit(Guid.CreateVersion7(), Day, 4m)],
            limit: 1);

        plan.Lines.Should().ContainSingle();
        plan.LimitReached.Should().BeTrue();
        plan.RemainingOutstanding.Should().Be(2m);
        plan.RemainingCredit.Should().Be(2m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Receivables_allocator_rejects_non_positive_limit(int limit)
        => ((Action)(() => ReceivablesFifoAllocator.Allocate([], [], limit)))
            .Should().Throw<NgbArgumentOutOfRangeException>();

    [Fact]
    public void Receivables_allocator_filters_non_positive_items_and_handles_empty_plan()
    {
        var plan = ReceivablesFifoAllocator.Allocate(
            [ReceivableCharge(Guid.CreateVersion7(), Day, 0m), ReceivableCharge(Guid.CreateVersion7(), Day, -1m)],
            [ReceivableCredit(Guid.CreateVersion7(), Day, 0m), ReceivableCredit(Guid.CreateVersion7(), Day, -1m)],
            limit: null);

        plan.Lines.Should().BeEmpty();
        plan.RemainingOutstanding.Should().Be(0m);
        plan.RemainingCredit.Should().Be(0m);
        plan.LimitReached.Should().BeFalse();
    }

    [Fact]
    public void Receivables_allocator_orders_fifo_skips_paid_charges_and_tracks_all_balances()
    {
        var firstCharge = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondCharge = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var thirdCharge = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var firstCredit = Guid.Parse("00000000-0000-0000-0000-000000000011");
        var secondCredit = Guid.Parse("00000000-0000-0000-0000-000000000012");

        var plan = ReceivablesFifoAllocator.Allocate(
            [
                ReceivableCharge(thirdCharge, Day.AddDays(1), 4m),
                ReceivableCharge(secondCharge, Day, 3m),
                ReceivableCharge(firstCharge, Day, 2m)
            ],
            [
                ReceivableCredit(secondCredit, Day.AddDays(1), 5m),
                ReceivableCredit(firstCredit, Day, 5m)
            ],
            limit: null);

        plan.Lines.Should().HaveCount(3);
        plan.Lines.Select(x => (x.CreditDocumentId, x.ChargeDocumentId, x.Amount)).Should().Equal(
            (firstCredit, firstCharge, 2m),
            (firstCredit, secondCharge, 3m),
            (secondCredit, thirdCharge, 4m));
        plan.RemainingOutstanding.Should().Be(0m);
        plan.RemainingCredit.Should().Be(1m);
        plan.LimitReached.Should().BeFalse();
    }

    [Fact]
    public void Receivables_allocator_stops_at_positive_limit()
    {
        var plan = ReceivablesFifoAllocator.Allocate(
            [ReceivableCharge(Guid.CreateVersion7(), Day, 2m), ReceivableCharge(Guid.CreateVersion7(), Day.AddDays(1), 2m)],
            [ReceivableCredit(Guid.CreateVersion7(), Day, 4m)],
            limit: 1);

        plan.Lines.Should().ContainSingle();
        plan.LimitReached.Should().BeTrue();
        plan.RemainingOutstanding.Should().Be(2m);
        plan.RemainingCredit.Should().Be(2m);
    }

    private static PayablesOpenChargeItemDetailsDto PayableCharge(Guid id, DateOnly due, decimal outstanding)
        => new(id, "pm.payable_charge", null, id.ToString(), due, null, null, null, null, outstanding, outstanding);

    private static PayablesOpenCreditItemDetailsDto PayableCredit(Guid id, DateOnly date, decimal available)
        => new(id, "pm.payable_payment", null, id.ToString(), date, null, available, available);

    private static ReceivablesOpenChargeItemDetailsDto ReceivableCharge(Guid id, DateOnly due, decimal outstanding)
        => new(id, "pm.receivable_charge", null, id.ToString(), due, null, null, null, outstanding, outstanding);

    private static ReceivablesOpenCreditItemDetailsDto ReceivableCredit(Guid id, DateOnly date, decimal available)
        => new(id, "pm.receivable_payment", null, id.ToString(), date, null, available, available);
}
