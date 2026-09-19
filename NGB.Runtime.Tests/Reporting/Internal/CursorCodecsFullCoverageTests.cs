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
    public void GeneralLedgerEncode_WhenAnyTotalIsMissing_UsesLegacyFormat(
        double? totalDebit,
        double? totalCredit,
        double? closingBalance)
    {
        var encoded = GeneralLedgerAggregatedCursorCodec.Encode(Cursor(
            totalDebit is null ? null : (decimal)totalDebit,
            totalCredit is null ? null : (decimal)totalCredit,
            closingBalance is null ? null : (decimal)closingBalance));

        encoded.Split('|').Should().HaveCount(6);
        encoded.Should().Contain("counter%2Faccount%20%7C%20special");
    }

    [Fact]
    public void GeneralLedgerEncodeAndDecode_CurrentAndLegacyFormatsRoundTrip()
    {
        var current = Cursor(100.25m, 60.5m, 49.75m);
        var currentEncoded = GeneralLedgerAggregatedCursorCodec.Encode(current);
        currentEncoded.Split('|').Should().HaveCount(10);
        GeneralLedgerAggregatedCursorCodec.Decode(currentEncoded).Should().BeEquivalentTo(current);

        var legacy = Cursor(null, null, null);
        var legacyDecoded = GeneralLedgerAggregatedCursorCodec.Decode(
            GeneralLedgerAggregatedCursorCodec.Encode(legacy));
        legacyDecoded.Should().BeEquivalentTo(legacy);
        legacyDecoded.AfterPeriodUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData("bad", "Invalid cursor format")]
    [InlineData("bad|11111111-1111-1111-1111-111111111111|code|22222222-2222-2222-2222-222222222222|33333333-3333-3333-3333-333333333333|1", "Invalid cursor timestamp")]
    [InlineData("2026-08-21T12:34:56Z|bad|code|22222222-2222-2222-2222-222222222222|33333333-3333-3333-3333-333333333333|1", "Invalid cursor document id")]
    [InlineData("2026-08-21T12:34:56Z|11111111-1111-1111-1111-111111111111|code|bad|33333333-3333-3333-3333-333333333333|1", "Invalid cursor counter account id")]
    [InlineData("2026-08-21T12:34:56Z|11111111-1111-1111-1111-111111111111|code|22222222-2222-2222-2222-222222222222|bad|1", "Invalid cursor dimension set id")]
    [InlineData("2026-08-21T12:34:56Z|11111111-1111-1111-1111-111111111111|code|22222222-2222-2222-2222-222222222222|33333333-3333-3333-3333-333333333333|bad", "Invalid cursor running balance")]
    [InlineData("2026-08-21T12:34:56Z|11111111-1111-1111-1111-111111111111|code|22222222-2222-2222-2222-222222222222|33333333-3333-3333-3333-333333333333|1|bad|2|3", "Invalid cursor total debit")]
    [InlineData("2026-08-21T12:34:56Z|11111111-1111-1111-1111-111111111111|code|22222222-2222-2222-2222-222222222222|33333333-3333-3333-3333-333333333333|1|2|bad|3", "Invalid cursor total credit")]
    [InlineData("2026-08-21T12:34:56Z|11111111-1111-1111-1111-111111111111|code|22222222-2222-2222-2222-222222222222|33333333-3333-3333-3333-333333333333|1|2|3|bad", "Invalid cursor closing balance")]
    public void GeneralLedgerDecode_RejectsEveryMalformedComponent(string value, string expectedMessage)
    {
        var action = () => GeneralLedgerAggregatedCursorCodec.Decode(value);

        action.Should().Throw<NgbArgumentInvalidException>().WithMessage($"*{expectedMessage}*");
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
