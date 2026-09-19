using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using NGB.Accounting.Reports.AccountCard;
using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Runtime.Reporting.Internal;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting.Internal;

public sealed class VersionedAccountingCursorTests
{
    private static readonly DateTime Period = new(2026, 9, 1, 13, 14, 15, DateTimeKind.Utc);
    private static readonly Guid Id = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Named_payload_preserves_session_precision_and_escaped_text_in_every_culture(bool withTotals)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var account = Account(withTotals);
            var ledger = Ledger(withTotals);
            AccountCardCursorCodec.Decode(AccountCardCursorCodec.Encode(account)).Should().BeEquivalentTo(account);
            GeneralLedgerAggregatedCursorCodec.Decode(GeneralLedgerAggregatedCursorCodec.Encode(ledger)).Should().BeEquivalentTo(ledger);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decoder_rejects_wrong_report_version_and_missing_required_payload_fields(bool ledger)
    {
        var cursor = ledger ? GeneralLedgerAggregatedCursorCodec.Encode(Ledger(true)) : AccountCardCursorCodec.Encode(Account(true));
        var normalized = cursor.Replace('-', '+').Replace('_', '/');
        var envelope = JsonNode.Parse(Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '=')))!;

        var mutations = new Action<JsonNode>[]
        {
            node => node["Version"] = -1,
            node => node["CursorKind"] = "another.report",
            node => node.AsObject().Remove("Payload"),
            node => node["Payload"] = null,
            node => node["Payload"]!["AfterPeriodUtc"] = "invalid date",
            node => node["Payload"]!["SnapshotId"] = "invalid session"
        };
        foreach (var mutate in mutations)
        {
            var invalid = envelope.DeepClone();
            mutate(invalid);
            Reject(invalid);
        }
        foreach (var field in envelope["Payload"]!.AsObject().Select(p => p.Key).ToArray())
        {
            var invalid = envelope.DeepClone();
            invalid["Payload"]!.AsObject().Remove(field);
            Reject(invalid);

            invalid = envelope.DeepClone();
            invalid["Payload"]![field] = new JsonArray();
            Reject(invalid);

            if (field != "Totals")
            {
                invalid = envelope.DeepClone();
                invalid["Payload"]![field] = null;
                Reject(invalid);
            }
        }
        foreach (var field in envelope["Payload"]!["Totals"]!.AsObject().Select(p => p.Key).ToArray())
        {
            var invalid = envelope.DeepClone();
            invalid["Payload"]!["Totals"]!.AsObject().Remove(field);
            Reject(invalid);

            invalid = envelope.DeepClone();
            invalid["Payload"]!["Totals"]![field] = "invalid amount";
            Reject(invalid);
        }
        void Reject(JsonNode invalid)
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(invalid.ToJsonString()));
            Action decode = () => { if (ledger) GeneralLedgerAggregatedCursorCodec.Decode(encoded); else AccountCardCursorCodec.Decode(encoded); };
            decode.Should().Throw<NgbArgumentInvalidException>();
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("|10.25|4.5|8.75")]
    [InlineData("|10.25|4.5|8.75|01234567-89ab-cdef-0123-456789abcdef")]
    public void Unversioned_positional_formats_are_rejected(string suffix)
    {
        Action account = () => AccountCardCursorCodec.Decode($"{Period:O}|42|3{suffix}");
        Action ledger = () => GeneralLedgerAggregatedCursorCodec.Decode($"{Period:O}|{Id:D}|counter%2Faccount%20%7C%20special|{Id:D}|{Id:D}|3{suffix}");
        account.Should().Throw<NgbArgumentInvalidException>().WithMessage("*format*");
        ledger.Should().Throw<NgbArgumentInvalidException>().WithMessage("*format*");
    }

    private static AccountCardReportCursor Account(bool totals) => new()
    {
        AfterPeriodUtc = Period, AfterEntryId = long.MaxValue, RunningBalance = decimal.MinValue, SnapshotId = Id,
        TotalDebit = totals ? decimal.MaxValue : null, TotalCredit = totals ? 0.0000000000000000000000000001m : null,
        ClosingBalance = totals ? -1.25m : null
    };

    private static GeneralLedgerAggregatedReportCursor Ledger(bool totals) => new()
    {
        AfterPeriodUtc = Period, AfterDocumentId = Id, AfterCounterAccountId = Id, AfterDimensionSetId = Id,
        AfterCounterAccountCode = "Счёт|/%\"", RunningBalance = decimal.MinValue, SnapshotId = Id,
        TotalDebit = totals ? decimal.MaxValue : null, TotalCredit = totals ? 0.0000000000000000000000000001m : null,
        ClosingBalance = totals ? -1.25m : null
    };
}
