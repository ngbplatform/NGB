using FluentAssertions;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Reports.AccountCard;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Accounting.Reports.LedgerAnalysis;
using NGB.Core.Dimensions;
using NGB.Runtime.Reporting.Internal;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting.Internal;

public sealed class ReportingInternalHelperFullCoverageTests
{
    [Fact]
    public void DisplayHelpers_CoverEmptyMissingBlankResolvedAndAccountDisplayShapes()
    {
        var dimensionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var valueId = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890");
        var bag = new DimensionBag([new DimensionValue(dimensionId, valueId)]);

        ReportDisplayHelpers.ShortGuid(valueId).Should().Be("abcdef12");
        DimensionBag.Empty.ToDimensionDisplayValues(null).Should().BeEmpty();
        DimensionBag.Empty.BuildDimensionSetDisplay(null).Should().Be("—");
        bag.ToDimensionDisplayValues(null).Should().Equal("abcdef12");
        bag.ToDimensionDisplayValues(new Dictionary<Guid, string>()).Should().Equal("abcdef12");
        bag.ToDimensionDisplayValues(new Dictionary<Guid, string> { [dimensionId] = "   " })
            .Should().Equal("abcdef12");
        bag.ToDimensionDisplayValues(new Dictionary<Guid, string> { [dimensionId] = " Party " })
            .Should().Equal("Party");
        bag.BuildDimensionSetDisplay(new Dictionary<Guid, string> { [dimensionId] = "Party" })
            .Should().Be("Party");

        ReportDisplayHelpers.BuildAccountDisplay(" ", null).Should().Be("—");
        ReportDisplayHelpers.BuildAccountDisplay(" ", " Name ").Should().Be("Name");
        ReportDisplayHelpers.BuildAccountDisplay(" 1000 ", " ").Should().Be("1000");
        ReportDisplayHelpers.BuildAccountDisplay(" 1000 ", " Cash ").Should().Be("1000 — Cash");
    }

    [Fact]
    public void AccountCardCursor_Encode_CoversEveryOptionalTotalCombination()
    {
        Cursor(totalDebit: null, totalCredit: null, closing: null).PipeCount().Should().Be(2);
        Cursor(totalDebit: 1m, totalCredit: null, closing: null).PipeCount().Should().Be(2);
        Cursor(totalDebit: 1m, totalCredit: 2m, closing: null).PipeCount().Should().Be(2);
        Cursor(totalDebit: 1m, totalCredit: 2m, closing: 3m).PipeCount().Should().Be(5);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a|b")]
    [InlineData("a|b|c|d")]
    public void AccountCardCursor_Decode_InvalidPartCount_Throws(string value)
    {
        Action action = () => AccountCardCursorCodec.Decode(value);

        action.Should().Throw<NgbArgumentInvalidException>().WithMessage("*format*");
    }

    [Theory]
    [InlineData("bad|1|2", "timestamp")]
    [InlineData("2026-01-01T00:00:00.0000000Z|bad|2", "entry id")]
    [InlineData("2026-01-01T00:00:00.0000000Z|1|bad", "running balance")]
    [InlineData("2026-01-01T00:00:00.0000000Z|1|2|bad|4|5", "total debit")]
    [InlineData("2026-01-01T00:00:00.0000000Z|1|2|3|bad|5", "total credit")]
    [InlineData("2026-01-01T00:00:00.0000000Z|1|2|3|4|bad", "closing balance")]
    public void AccountCardCursor_Decode_InvalidComponent_Throws(string value, string message)
    {
        Action action = () => AccountCardCursorCodec.Decode(value);

        action.Should().Throw<NgbArgumentInvalidException>().WithMessage($"*{message}*");
    }

    [Fact]
    public void AccountCardCursor_RoundTripsLegacyAndCurrentFormats()
    {
        var legacy = Cursor(totalDebit: null, totalCredit: null, closing: null);
        var current = Cursor(totalDebit: 11m, totalCredit: 12m, closing: 13m);

        var decodedLegacy = AccountCardCursorCodec.Decode(AccountCardCursorCodec.Encode(legacy));
        var decodedCurrent = AccountCardCursorCodec.Decode(AccountCardCursorCodec.Encode(current));

        decodedLegacy.Should().BeEquivalentTo(legacy);
        decodedLegacy.AfterPeriodUtc.Kind.Should().Be(DateTimeKind.Utc);
        decodedCurrent.Should().BeEquivalentTo(current);
    }

    [Theory]
    [InlineData("", "format")]
    [InlineData("bad|1|debit", "timestamp")]
    [InlineData("2026-01-01T00:00:00.0000000Z|bad|debit", "entry id")]
    [InlineData("2026-01-01T00:00:00.0000000Z|1|%20", "posting side")]
    public void LedgerAnalysisCursor_InvalidValue_Throws(string value, string message)
    {
        Action action = () => LedgerAnalysisDetailCursorCodec.Decode(value);

        action.Should().Throw<NgbArgumentInvalidException>().WithMessage($"*{message}*");
    }

    [Fact]
    public void LedgerAnalysisCursor_RoundTripsEscapedPostingSide()
    {
        var cursor = new LedgerAnalysisFlatDetailCursor(Utc, 42, "debit side/+");

        var decoded = LedgerAnalysisDetailCursorCodec.Decode(LedgerAnalysisDetailCursorCodec.Encode(cursor));

        decoded.Should().BeEquivalentTo(cursor);
        decoded.AfterPeriodUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData("", "format")]
    [InlineData("bad|11111111-1111-1111-1111-111111111111|1", "timestamp")]
    [InlineData("2026-01-01T00:00:00.0000000Z|bad|1", "document id")]
    [InlineData("2026-01-01T00:00:00.0000000Z|11111111-1111-1111-1111-111111111111|bad", "operation")]
    public void PostingLogCursor_InvalidValue_Throws(string value, string message)
    {
        Action action = () => PostingLogCursorCodec.Decode(value);

        action.Should().Throw<NgbArgumentInvalidException>().WithMessage($"*{message}*");
    }

    [Fact]
    public void PostingLogCursor_RoundTrips()
    {
        var cursor = new PostingStateCursor(Utc, Guid.Parse("11111111-1111-1111-1111-111111111111"), 3);

        var decoded = PostingLogCursorCodec.Decode(PostingLogCursorCodec.Encode(cursor));

        decoded.Should().BeEquivalentTo(cursor);
        decoded.AfterStartedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData("", "format")]
    [InlineData("bad|1", "timestamp")]
    [InlineData("2026-01-01T00:00:00.0000000Z|bad", "entry id")]
    public void GeneralJournalCursor_InvalidValue_Throws(string value, string message)
    {
        Action action = () => GeneralJournalCursorCodec.Decode(value);

        action.Should().Throw<NgbArgumentInvalidException>().WithMessage($"*{message}*");
    }

    [Fact]
    public void GeneralJournalCursor_RoundTrips()
    {
        var cursor = new GeneralJournalCursor(Utc, 42);

        var decoded = GeneralJournalCursorCodec.Decode(GeneralJournalCursorCodec.Encode(cursor));

        decoded.Should().BeEquivalentTo(cursor);
        decoded.AfterPeriodUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    private static AccountCardReportCursor Cursor(
        decimal? totalDebit,
        decimal? totalCredit,
        decimal? closing)
        => new()
        {
            AfterPeriodUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            AfterEntryId = 42,
            RunningBalance = 10m,
            TotalDebit = totalDebit,
            TotalCredit = totalCredit,
            ClosingBalance = closing
        };

    private static DateTime Utc => new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
}

internal static class CursorCoverageExtensions
{
    public static int PipeCount(this AccountCardReportCursor cursor)
        => AccountCardCursorCodec.Encode(cursor).Count(character => character == '|');
}
