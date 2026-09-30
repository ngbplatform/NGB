using FluentAssertions;
using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Runtime.Reporting.Internal;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting.Internal;

public sealed class CursorCodecsFullCoverageTests
{
    private static readonly DateTime PeriodUtc = new(2026, 8, 21, 12, 34, 56, DateTimeKind.Utc);
    private static readonly Guid DocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CounterAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DimensionSetId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Theory]
    [InlineData(null, null, null)]
    [InlineData(10.0, null, null)]
    [InlineData(10.0, 20.0, null)]
    public void GeneralLedgerEncode_WhenAnyTotalIsMissing_OmitsAllTotals(
        double? totalDebit,
        double? totalCredit,
        double? closingBalance)
    {
        var encoded = GeneralLedgerAggregatedCursorCodec.Encode(Cursor(
            totalDebit is null ? null : (decimal)totalDebit,
            totalCredit is null ? null : (decimal)totalCredit,
            closingBalance is null ? null : (decimal)closingBalance));

        var decoded = GeneralLedgerAggregatedCursorCodec.Decode(encoded);
        decoded.TotalDebit.Should().BeNull();
        decoded.TotalCredit.Should().BeNull();
        decoded.ClosingBalance.Should().BeNull();
        decoded.AfterCounterAccountCode.Should().Be("counter/account | special");
    }

    [Fact]
    public void GeneralLedgerEncodeAndDecode_RoundTripWithAndWithoutTotals()
    {
        var current = Cursor(100.25m, 60.5m, 49.75m);
        var currentEncoded = GeneralLedgerAggregatedCursorCodec.Encode(current);
        GeneralLedgerAggregatedCursorCodec.Decode(currentEncoded).Should().BeEquivalentTo(current);

        var withoutTotals = Cursor(null, null, null);
        var decoded = GeneralLedgerAggregatedCursorCodec.Decode(
            GeneralLedgerAggregatedCursorCodec.Encode(withoutTotals));
        decoded.Should().BeEquivalentTo(withoutTotals);
        decoded.AfterPeriodUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("bad")]
    public void GeneralLedgerDecode_RejectsMalformedTokens(string? value)
    {
        var action = () => GeneralLedgerAggregatedCursorCodec.Decode(value!);

        action.Should().Throw<NgbArgumentInvalidException>().WithMessage("*format*");
    }

    private static GeneralLedgerAggregatedReportCursor Cursor(
        decimal? totalDebit,
        decimal? totalCredit,
        decimal? closingBalance) => new()
    {
        AfterPeriodUtc = PeriodUtc,
        AfterDocumentId = DocumentId,
        AfterCounterAccountCode = "counter/account | special",
        AfterCounterAccountId = CounterAccountId,
        AfterDimensionSetId = DimensionSetId,
        RunningBalance = 10.5m,
        TotalDebit = totalDebit,
        TotalCredit = totalCredit,
        ClosingBalance = closingBalance
    };
}
