using FluentAssertions;
using NGB.PropertyManagement.BackgroundJobs.Services;
using Xunit;

namespace NGB.PropertyManagement.BackgroundJobs.Tests.Jobs;

public sealed class MonthlyRentChargePlanner_P0Tests
{
    [Fact]
    public void BuildCandidates_ClipsPeriodsToLeaseBounds_AndBackfillsOnlyDueMonths()
    {
        var lease = new PmRentChargeGenerationLease(
            LeaseId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            StartOnUtc: new DateOnly(2026, 2, 15),
            EndOnUtc: null,
            RentAmount: 1750.00m,
            DueDay: 5);

        var result = MonthlyRentChargePlanner.BuildCandidates(lease, new DateOnly(2026, 4, 6));

        result.Should().BeEquivalentTo(
            [
                new
                {
                    LeaseId = lease.LeaseId,
                    PeriodFromUtc = new DateOnly(2026, 2, 15),
                    PeriodToUtc = new DateOnly(2026, 2, 28),
                    DueOnUtc = new DateOnly(2026, 2, 15),
                    Amount = 1750.00m,
                    Memo = "Monthly rent for February 2026."
                },
                new
                {
                    LeaseId = lease.LeaseId,
                    PeriodFromUtc = new DateOnly(2026, 3, 1),
                    PeriodToUtc = new DateOnly(2026, 3, 31),
                    DueOnUtc = new DateOnly(2026, 3, 5),
                    Amount = 1750.00m,
                    Memo = "Monthly rent for March 2026."
                },
                new
                {
                    LeaseId = lease.LeaseId,
                    PeriodFromUtc = new DateOnly(2026, 4, 1),
                    PeriodToUtc = new DateOnly(2026, 4, 6),
                    DueOnUtc = new DateOnly(2026, 4, 5),
                    Amount = 1750.00m,
                    Memo = "Monthly rent for April 2026."
                }
            ],
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void BuildCandidates_WhenCurrentMonthDueDateHasNotArrived_SkipsCurrentMonth()
    {
        var lease = new PmRentChargeGenerationLease(
            LeaseId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            StartOnUtc: new DateOnly(2026, 4, 1),
            EndOnUtc: null,
            RentAmount: 2100.00m,
            DueDay: 10);

        var result = MonthlyRentChargePlanner.BuildCandidates(lease, new DateOnly(2026, 4, 9));

        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildCandidates_WhenDueDayIsMissing_DefaultsToPeriodStart()
    {
        var lease = new PmRentChargeGenerationLease(
            LeaseId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            StartOnUtc: new DateOnly(2026, 2, 20),
            EndOnUtc: new DateOnly(2026, 2, 28),
            RentAmount: 900.00m,
            DueDay: null);

        var result = MonthlyRentChargePlanner.BuildCandidates(lease, new DateOnly(2026, 2, 28));

        result.Should().ContainSingle()
            .Which.DueOnUtc.Should().Be(new DateOnly(2026, 2, 20));
    }
}
