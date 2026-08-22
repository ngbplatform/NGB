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
        currentEncoded.Split('|').Should().HaveCount(9);
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

    [Fact]
    public void RenderedSheetEncode_ValidatesBoundariesAndFormatsBothVersions()
    {
        Action negativeOffset = () => RenderedSheetCursorCodec.EncodeOffsetOnly(-1);
        negativeOffset.Should().Throw<NgbArgumentInvalidException>();
        RenderedSheetCursorCodec.EncodeOffsetOnly(0).Should().Be("v1|0");

        var snapshotId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var fingerprint = Guid.Parse("55555555-5555-5555-5555-555555555555");
        Action emptySnapshot = () => RenderedSheetCursorCodec.EncodeSnapshot(Guid.Empty, 0, fingerprint);
        Action emptyFingerprint = () => RenderedSheetCursorCodec.EncodeSnapshot(snapshotId, 0, Guid.Empty);
        Action negativeSnapshotOffset = () => RenderedSheetCursorCodec.EncodeSnapshot(snapshotId, -1, fingerprint);
        emptySnapshot.Should().Throw<NgbArgumentInvalidException>();
        emptyFingerprint.Should().Throw<NgbArgumentInvalidException>();
        negativeSnapshotOffset.Should().Throw<NgbArgumentInvalidException>();

        RenderedSheetCursorCodec.EncodeSnapshot(snapshotId, 7, fingerprint)
            .Should().Be($"v2|{snapshotId:D}|7|{fingerprint:D}");
    }

    [Fact]
    public void RenderedSheetDecode_AcceptsTrimmedCaseInsensitiveVersions()
    {
        RenderedSheetCursorCodec.Decode(" V1 | 7 ").Should()
            .Be(new RenderedSheetCursor(7, null, null));

        var snapshotId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var fingerprint = Guid.Parse("55555555-5555-5555-5555-555555555555");
        RenderedSheetCursorCodec.Decode($" V2 | {snapshotId:D} | 9 | {fingerprint:D} ").Should()
            .Be(new RenderedSheetCursor(9, snapshotId, fingerprint));
    }

    [Theory]
    [InlineData("v1|bad", "Invalid cursor offset")]
    [InlineData("v1|-1", "Invalid cursor offset")]
    [InlineData("v2|bad|0|55555555-5555-5555-5555-555555555555", "Invalid cursor snapshot id")]
    [InlineData("v2|00000000-0000-0000-0000-000000000000|0|55555555-5555-5555-5555-555555555555", "Invalid cursor snapshot id")]
    [InlineData("v2|44444444-4444-4444-4444-444444444444|bad|55555555-5555-5555-5555-555555555555", "Invalid cursor offset")]
    [InlineData("v2|44444444-4444-4444-4444-444444444444|-1|55555555-5555-5555-5555-555555555555", "Invalid cursor offset")]
    [InlineData("v2|44444444-4444-4444-4444-444444444444|0|bad", "Invalid cursor fingerprint")]
    [InlineData("v2|44444444-4444-4444-4444-444444444444|0|00000000-0000-0000-0000-000000000000", "Invalid cursor fingerprint")]
    [InlineData("v3|0", "Invalid cursor format")]
    [InlineData("v2|too|short", "Invalid cursor format")]
    public void RenderedSheetDecode_RejectsEveryMalformedComponent(string value, string expectedMessage)
    {
        var action = () => RenderedSheetCursorCodec.Decode(value);

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
