using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Posting;

public sealed class PostingGuardCoverageTests
{
    [Fact]
    public void AccountingPostingContext_EmptyDocumentId_ThrowsOutOfRange()
    {
        var context = new AccountingPostingContext(new ChartOfAccounts());

        Action action = () => context.Post(Guid.Empty, Utc, Account(), Account(), 1m);

        action.Should().Throw<NgbArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("documentId");
    }

    [Fact]
    public async Task AccountingPostingContext_NullDimensions_DefaultBothSidesToEmptyAndExposesChart()
    {
        var chart = new ChartOfAccounts();
        var context = new AccountingPostingContext(chart);

        context.Post(Guid.CreateVersion7(), Utc, Account(), Account(), 1m);

        context.Entries.Should().ContainSingle();
        context.Entries[0].DebitDimensions.Should().BeSameAs(NGB.Core.Dimensions.DimensionBag.Empty);
        context.Entries[0].CreditDimensions.Should().BeSameAs(NGB.Core.Dimensions.DimensionBag.Empty);
        (await context.GetChartOfAccountsAsync()).Should().BeSameAs(chart);
    }

    [Fact]
    public void AccountingPostingContext_NullDebit_ThrowsArgumentRequired()
    {
        var context = new AccountingPostingContext(new ChartOfAccounts());

        Action action = () => context.Post(
            Guid.CreateVersion7(),
            Utc,
            null!,
            Account(),
            1m);

        action.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("debit");
    }

    [Fact]
    public void AccountingPostingContext_NullCredit_ThrowsArgumentRequired()
    {
        var context = new AccountingPostingContext(new ChartOfAccounts());

        Action action = () => context.Post(
            Guid.CreateVersion7(),
            Utc,
            Account(),
            null!,
            1m);

        action.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("credit");
    }

    [Fact]
    public async Task RepostingService_NullPostingDelegate_ThrowsArgumentRequired()
    {
        var service = RepostingService();

        var action = () => service.RepostAsync(Guid.CreateVersion7(), null!);

        var exception = await action.Should().ThrowAsync<NgbArgumentRequiredException>();
        exception.Which.ParamName.Should().Be("postNew");
    }

    [Fact]
    public async Task RepostingService_EmptyDocumentId_ThrowsOutOfRange()
    {
        var service = RepostingService();

        var action = () => service.RepostAsync(Guid.Empty, (_, _) => Task.CompletedTask);

        var exception = await action.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        exception.Which.ParamName.Should().Be("documentId");
    }

    private static DateTime Utc => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Account Account()
        => new(
            Guid.CreateVersion7(),
            "1000",
            "Account",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy.Allow);

    private static RepostingService RepostingService()
        => new(null!, null!, null!, null!);
}
