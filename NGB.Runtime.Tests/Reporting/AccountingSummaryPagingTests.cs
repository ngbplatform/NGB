using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class AccountingSummaryPagingTests
{
    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void Equity_section_validation_accepts_only_equity(int group, bool expected)
    {
        var method = typeof(AccountingSummaryPagedExecutor).GetMethod("ValidGroup", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        method.Invoke(null, [AccountingSummaryKind.Equity, group]).Should().Be(expected);
    }

    [Theory]
    [InlineData(AccountingReportCodes.TrialBalance, -1)]
    [InlineData(AccountingReportCodes.TrialBalance, 5)]
    [InlineData(AccountingReportCodes.BalanceSheet, 0)]
    [InlineData(AccountingReportCodes.BalanceSheet, 4)]
    [InlineData(AccountingReportCodes.IncomeStatement, 3)]
    [InlineData(AccountingReportCodes.IncomeStatement, 9)]
    [InlineData(AccountingReportCodes.StatementOfChangesInEquity, 3)]
    public async Task Invalid_sections_are_rejected_before_reading(string code, int group)
    {
        var reader = new Mock<IAccountingSummaryPageReader>(MockBehavior.Strict);
        var action = () => new AccountingSummaryPagedExecutor(reader.Object).ExecuteAsync(new(code, code), Request() with { GroupPath = [JsonSerializer.SerializeToElement(group)] }, default);
        await action.Should().ThrowAsync<NgbArgumentInvalidException>().WithMessage("*section*");
        reader.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("\"1\"")]
    [InlineData("1.5")]
    [InlineData("2147483648")]
    public async Task Section_keys_must_be_integers(string json)
    {
        var action = () => new AccountingSummaryPagedExecutor(Mock.Of<IAccountingSummaryPageReader>()).ExecuteAsync(
            new(AccountingReportCodes.TrialBalance, "Trial"), Request() with { GroupPath = [JsonDocument.Parse(json).RootElement] }, default);
        await action.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Theory]
    [InlineData("!")]
    [InlineData("json:{")]
    [InlineData("json:null")]
    [InlineData("json:{}")]
    public async Task Malformed_account_continuations_are_rejected(string cursor)
    {
        var action = () => new AccountingSummaryPagedExecutor(Mock.Of<IAccountingSummaryPageReader>()).ExecuteAsync(
            new(AccountingReportCodes.TrialBalance, "Trial"), Request() with { GroupPath = [JsonSerializer.SerializeToElement(0)], Cursor = Encode(cursor) }, default);
        await action.Should().ThrowAsync<NgbArgumentInvalidException>().WithMessage("*cursor*");
    }

    [Fact]
    public async Task Root_sections_cannot_be_continued_and_account_codes_are_bounded()
    {
        var executor = new AccountingSummaryPagedExecutor(Mock.Of<IAccountingSummaryPageReader>());
        var definition = new ReportDefinitionDto(AccountingReportCodes.TrialBalance, "Trial");
        await ((Func<Task>)(() => executor.ExecuteAsync(definition, Request() with { Cursor = "unused" }, default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        var cursor = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new AccountingSummaryKey(new string('x', 501), Guid.NewGuid())));
        await ((Func<Task>)(() => executor.ExecuteAsync(definition, Request() with { Cursor = cursor, GroupPath = [JsonSerializer.SerializeToElement(0)] }, default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Theory]
    [InlineData(AccountingReportCodes.TrialBalance, 3, false)]
    [InlineData(AccountingReportCodes.TrialBalance, 3, true)]
    [InlineData(AccountingReportCodes.TrialBalance, 0, false)]
    [InlineData(AccountingReportCodes.TrialBalance, 0, true)]
    [InlineData(AccountingReportCodes.BalanceSheet, 3, false)]
    [InlineData(AccountingReportCodes.BalanceSheet, 3, true)]
    [InlineData(AccountingReportCodes.BalanceSheet, 1, false)]
    [InlineData(AccountingReportCodes.IncomeStatement, 4, false)]
    [InlineData(AccountingReportCodes.StatementOfChangesInEquity, 3, false)]
    [InlineData(AccountingReportCodes.StatementOfChangesInEquity, 3, true)]
    public async Task Terminal_accounts_obey_total_preferences_and_keep_equity_earnings(string code, int group, bool totals)
    {
        var equity = code == AccountingReportCodes.StatementOfChangesInEquity;
        var reader = Reader([Value(4, opening: -7, closing: 0)]);
        reader.Setup(x => x.ReadAccountsAsync(It.IsAny<AccountingSummaryQuery>(), group, null, It.IsAny<int>(), default))
            .ReturnsAsync(new AccountingSummaryPage([Value(group)], false, null));
        var result = await new AccountingSummaryPagedExecutor(reader.Object).ExecuteAsync(new(code, "Report"), Request() with
        { Layout = new(ShowGrandTotals: totals), GroupPath = equity ? null : [JsonSerializer.SerializeToElement(group)] }, default);
        result.HasMore.Should().BeFalse();
        result.Sheet.Rows.Count(r => r.RowKind == ReportRowKind.Total).Should().Be(totals ? 1 : 0);
        result.Sheet.Rows.Count(r => r.RowKind == ReportRowKind.Detail).Should().Be(equity ? 2 : 1);
        if (equity)
        {
            var earnings = result.Sheet.Rows.Last(r => r.RowKind == ReportRowKind.Detail);
            earnings.Cells[0].Display.Should().Be("Current Earnings (Unclosed)");
            earnings.Cells[0].Action.Should().BeNull();
            earnings.Cells[1].Value!.Value.GetDecimal().Should().Be(7);
        }
    }

    [Fact]
    public async Task Balance_sheet_equity_includes_profit_and_omits_invalid_sections()
    {
        var reader = Reader([Value(3, closing: -10), Value(4, closing: -7), Value(0)]);
        var result = await new AccountingSummaryPagedExecutor(reader.Object).ExecuteAsync(new(AccountingReportCodes.BalanceSheet, "Balance"), Request(), default);
        result.Sheet.Rows.Single(r => r.RowKind == ReportRowKind.Group).Cells[1].Value!.Value.GetDecimal().Should().Be(17);
    }

    [Fact]
    public async Task Trial_balance_account_footer_preserves_the_selected_sections_turnovers()
    {
        var group = new AccountingSummaryValue(0, Guid.NewGuid(), "100", "Cash", 1, 12, 4, 9);
        var reader = Reader([group]);
        reader.Setup(x => x.ReadAccountsAsync(It.IsAny<AccountingSummaryQuery>(), 0, null, It.IsAny<int>(), default))
            .ReturnsAsync(new AccountingSummaryPage([group], false, null));
        var page = await new AccountingSummaryPagedExecutor(reader.Object).ExecuteAsync(new(AccountingReportCodes.TrialBalance, "Trial"),
            Request() with { GroupPath = [JsonSerializer.SerializeToElement(0)] }, default);
        var footer = page.Sheet.Rows.Single(r => r.RowKind == ReportRowKind.Total);
        footer.Cells[1].Value!.Value.GetDecimal().Should().Be(12);
        footer.Cells[2].Value!.Value.GetDecimal().Should().Be(4);
    }

    [Theory]
    [InlineData(AccountingReportCodes.TrialBalance, 0)]
    [InlineData(AccountingReportCodes.BalanceSheet, 3)]
    [InlineData(AccountingReportCodes.StatementOfChangesInEquity, 3)]
    public async Task Intermediate_account_pages_return_a_cursor_without_fetching_footer_totals(string code, int group)
    {
        var reader = new Mock<IAccountingSummaryPageReader>(MockBehavior.Strict);
        var next = new AccountingSummaryKey("100", Guid.NewGuid());
        reader.Setup(x => x.ReadAccountsAsync(It.IsAny<AccountingSummaryQuery>(), group, null, It.IsAny<int>(), default))
            .ReturnsAsync(new AccountingSummaryPage([Value(group)], true, next));
        var page = await new AccountingSummaryPagedExecutor(reader.Object).ExecuteAsync(new(code, "Report"),
            Request() with { GroupPath = code == AccountingReportCodes.StatementOfChangesInEquity ? null : [JsonSerializer.SerializeToElement(group)] }, default);
        page.HasMore.Should().BeTrue();
        JsonSerializer.Deserialize<AccountingSummaryKey>(Convert.FromBase64String(page.NextCursor!)).Should().Be(next);
        page.Sheet.Rows.Should().OnlyContain(r => r.RowKind == ReportRowKind.Detail);
        reader.Verify(x => x.ReadGroupsAsync(It.IsAny<AccountingSummaryQuery>(), default), Times.Never);
    }

    private static Mock<IAccountingSummaryPageReader> Reader(IReadOnlyList<AccountingSummaryValue> groups)
    {
        var reader = new Mock<IAccountingSummaryPageReader>(MockBehavior.Strict);
        reader.Setup(x => x.ReadGroupsAsync(It.IsAny<AccountingSummaryQuery>(), default)).ReturnsAsync(groups);
        return reader;
    }
    private static AccountingSummaryValue Value(int group, decimal opening = 0, decimal closing = 0) => new(group, Guid.NewGuid(), "100", "Account", opening, 0, 0, closing);
    private static string Encode(string value) => value.StartsWith("json:") ? Convert.ToBase64String(Encoding.UTF8.GetBytes(value[5..])) : value;
    private static ReportExecutionRequestDto Request() => new(Parameters: new Dictionary<string, string>
    { ["from_utc"] = "2026-09-01", ["to_utc"] = "2026-09-30", ["as_of_utc"] = "2026-09-30" });
}
