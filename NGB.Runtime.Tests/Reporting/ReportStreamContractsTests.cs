using FluentAssertions;
using Microsoft.Extensions.Options;
using NGB.Accounting.Reports;
using NGB.Accounting.Reports.AccountCard;
using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Accounting.Reports.TrialBalance;
using NGB.Persistence.Reporting;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Internal;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportStreamContractsTests
{
    [Fact]
    public async Task Metadata_buffer_preserves_write_order_and_leaves_destination_open()
    {
        using var destination = new MemoryStream();
        await using (var buffer = new AsyncExportBuffer(destination, default))
        {
            buffer.CanRead.Should().BeFalse();
            buffer.CanSeek.Should().BeFalse();
            buffer.CanWrite.Should().BeTrue();
            buffer.Write([1, 2], 0, 2);
            buffer.Flush();
            destination.Length.Should().Be(0);
            await buffer.WriteAsync(new byte[] { 0, 3, 4, 0 }, 1, 2, default);
            buffer.Write([5], 0, 1);
            await buffer.FlushAsync(default);
            buffer.Write([6], 0, 1);
            Assert.Throws<NotSupportedException>(() => buffer.Length);
            Assert.Throws<NotSupportedException>(() => buffer.Position);
            Assert.Throws<NotSupportedException>(() => buffer.Position = 0);
            Assert.Throws<NotSupportedException>(() => buffer.Read(new byte[1], 0, 1));
            Assert.Throws<NotSupportedException>(() => buffer.Seek(0, SeekOrigin.Begin));
            Assert.Throws<NotSupportedException>(() => buffer.SetLength(0));
        }
        destination.ToArray().Should().Equal(1, 2, 3, 4, 5, 6);
        destination.CanWrite.Should().BeTrue();
    }

    [Fact]
    public async Task Metadata_overflow_is_rejected_without_losing_already_buffered_bytes()
    {
        using var destination = new MemoryStream();
        await using var buffer = new AsyncExportBuffer(destination, default);
        buffer.Write(new byte[65536]);
        Assert.Throws<IOException>(() => buffer.Write(new byte[1]));
        await buffer.FlushAsync(default);
        destination.Length.Should().Be(65536);
        buffer.Write(new byte[1]);
        await buffer.FlushAsync(default);
        destination.Length.Should().Be(65537);
    }

    [Fact]
    public void Activity_query_rejects_missing_account_and_reversed_range()
    {
        var from = new DateOnly(2026, 9, 1);
        Assert.Throws<NgbArgumentRequiredException>(() => new AccountActivityQuery(Guid.Empty, from, from, null).EnsureInvariant());
        Assert.Throws<NgbArgumentOutOfRangeException>(() => new AccountActivityQuery(Guid.NewGuid(), from, from.AddMonths(-1), null).EnsureInvariant());
        new AccountActivityQuery(Guid.NewGuid(), from, from, null).EnsureInvariant();
        new TrialBalanceReportPage([], 1, true, new(0, 0, 0, 0)).HasMore.Should().BeTrue();
        IReportReadSession session = new DefaultSession();
        session.SnapshotId.Should().BeEmpty();
    }

    [Fact]
    public void Cursor_protection_rejects_weak_keys_and_malformed_envelopes()
    {
        Assert.Throws<ArgumentException>(() => new ReportCursorProtector(Options.Create(new ReportCursorProtectionOptions
        { SigningKey = Convert.ToBase64String(new byte[31]) })));
        using var protector = new ReportCursorProtector(Options.Create(new ReportCursorProtectionOptions()));
        foreach (var invalid in new[] { new string('x', 131073), "wrong:body", "rq2:body", "rq2:a.b.c" })
            Assert.Throws<FormatException>(() => protector.Unprotect(invalid));
    }

    [Fact]
    public void Accounting_cursors_require_payloads_and_normalize_local_timestamps_to_utc()
    {
        Assert.Throws<NgbArgumentRequiredException>(() => AccountCardCursorCodec.Encode(null!));
        Assert.Throws<NgbArgumentRequiredException>(() => GeneralLedgerAggregatedCursorCodec.Encode(null!));
        var local = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Local);
        var account = new AccountCardReportCursor { AfterPeriodUtc = local, AfterEntryId = 1 };
        var ledger = new GeneralLedgerAggregatedReportCursor { AfterPeriodUtc = local, AfterDocumentId = Guid.NewGuid(), AfterCounterAccountId = Guid.NewGuid(), AfterDimensionSetId = Guid.NewGuid(), AfterCounterAccountCode = "100" };
        AccountCardCursorCodec.Decode(AccountCardCursorCodec.Encode(account)).AfterPeriodUtc.Should().Be(local.ToUniversalTime());
        GeneralLedgerAggregatedCursorCodec.Decode(GeneralLedgerAggregatedCursorCodec.Encode(ledger)).AfterPeriodUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    private sealed class DefaultSession : IReportReadSession
    {
        public Task BeginAsync(CancellationToken ct) => Task.CompletedTask;
        public Task EndAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
