using FluentAssertions;
using NGB.Persistence.Periods;
using Xunit;

namespace NGB.Runtime.Tests.Periods;

public sealed class ClosedPeriodRepositoryCompatibilityTests
{
    [Theory]
    [InlineData(2026, 1, 2026, 2)]
    [InlineData(2026, 1, 2030, 1)]
    [InlineData(9999, 12, 9999, 12)]
    public async Task ExistingAdapter_WithoutNewMethod_FindsClosedBoundary(
        int year, int month, int closedYear, int closedMonth)
    {
        var period = new DateOnly(year, month, 15);
        var closed = new DateOnly(closedYear, closedMonth, 1);
        IClosedPeriodRepository repository = new LegacyClosedPeriodRepository(
            new DateOnly(year, month, 1).AddMonths(-1), closed);

        (await repository.FindFirstClosedOnOrAfterAsync(period)).Should().Be(closed);
    }

    [Fact]
    public async Task ExistingAdapter_WithOnlyEarlierClosedMonths_DoesNotBlockFuturePosting()
    {
        IClosedPeriodRepository repository = new LegacyClosedPeriodRepository(new DateOnly(2026, 1, 1));

        (await repository.FindFirstClosedOnOrAfterAsync(new DateOnly(2026, 2, 1))).Should().BeNull();
    }

    // Implements only the interface members required before the posting-boundary fix.
    private sealed class LegacyClosedPeriodRepository(params DateOnly[] closedPeriods) : IClosedPeriodRepository
    {
        private readonly HashSet<DateOnly> _closedPeriods = closedPeriods.ToHashSet();

        public Task<bool> IsClosedAsync(DateOnly period, CancellationToken ct = default)
            => Task.FromResult(_closedPeriods.Contains(period));

        public Task<DateOnly?> FindFirstClosedAsync(
            IReadOnlyCollection<DateOnly> periods,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(periods.Where(_closedPeriods.Contains).Order().Cast<DateOnly?>().FirstOrDefault());
        }

        public Task MarkClosedAsync(
            DateOnly period,
            string closedBy,
            DateTime closedAtUtc,
            CancellationToken ct = default)
        {
            _closedPeriods.Add(period);
            return Task.CompletedTask;
        }

        public Task ReopenAsync(DateOnly period, CancellationToken ct = default)
        {
            _closedPeriods.Remove(period);
            return Task.CompletedTask;
        }
    }
}
