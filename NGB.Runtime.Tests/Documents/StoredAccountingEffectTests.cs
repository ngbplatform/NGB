using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Registers;
using NGB.Runtime.Documents.Posting;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class StoredAccountingEffectTests
{
    private static readonly Guid DocumentId = Guid.NewGuid();
    private static readonly Account Cash = new(Guid.NewGuid(), "50", "Cash", AccountType.Asset);
    private static readonly Account Revenue = new(Guid.NewGuid(), "90", "Revenue", AccountType.Income);
    private static readonly DateTime Period = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EmptyHistory_AndOrdinaryOpposingLines_ArePreserved()
    {
        StoredAccountingEffect.GetEntriesToReverse([]).Should().BeEmpty();
        var history = new[] { Entry(1), Entry(2, reverse: true) };

        StoredAccountingEffect.GetEntriesToReverse(history).Should().BeSameAs(history);
    }

    [Fact]
    public void RepostHistory_RemovesOnlyMatchedStornoPairs_AndKeepsDuplicateLiveLines()
    {
        var first = Entry(1);
        var storno = Entry(2, reverse: true, isStorno: true);
        var replacement1 = Entry(3, amount: 250m);
        var replacement2 = Entry(4, amount: 250m);

        var remaining = StoredAccountingEffect.GetEntriesToReverse([replacement2, storno, first, replacement1]);

        remaining.Should().Equal(replacement1, replacement2);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("period")]
    [InlineData("document")]
    [InlineData("debitDimensions")]
    [InlineData("creditDimensions")]
    public void DifferentStoredKeys_AreNotMistakenForCancelledPairs(string difference)
    {
        var original = Entry(1);
        var storno = Entry(
            2,
            reverse: true,
            isStorno: true,
            amount: difference == "amount" ? 50m : 100m,
            period: difference == "period" ? Period.AddDays(1) : Period,
            documentId: difference == "document" ? Guid.NewGuid() : DocumentId,
            debitDimensions: difference == "debitDimensions" ? Guid.NewGuid() : Guid.Empty,
            creditDimensions: difference == "creditDimensions" ? Guid.NewGuid() : Guid.Empty);
        var history = new[] { original, storno };

        StoredAccountingEffect.GetEntriesToReverse(history).Should().BeSameAs(history);
    }

    [Fact]
    public void LegacyStornoOfStornoHistory_IsCancelledWithoutChangingTheRemainingEntry()
    {
        var replacement = Entry(7, amount: 275m);
        var history = new[]
        {
            Entry(1),
            Entry(2, reverse: true, isStorno: true),
            Entry(3, amount: 250m),
            Entry(4, reverse: true, isStorno: true),
            Entry(5, isStorno: true),
            Entry(6, reverse: true, isStorno: true, amount: 250m),
            replacement
        };

        StoredAccountingEffect.GetEntriesToReverse(history).Should().ContainSingle().Which.Should().BeSameAs(replacement);
    }

    [Fact]
    public void ZeroEffect_KeepsOnlyTheLastZeroNetPair_ForTheAccountingOperation()
    {
        var latestOriginal = Entry(3, amount: 250m);
        var latestStorno = Entry(4, reverse: true, isStorno: true, amount: 250m);

        var remaining = StoredAccountingEffect.GetEntriesToReverse(
            [Entry(1), Entry(2, reverse: true, isStorno: true), latestOriginal, latestStorno]);

        remaining.Should().Equal(latestOriginal, latestStorno);
        remaining.Sum(entry => entry.Debit.Id == Cash.Id ? entry.Amount : -entry.Amount).Should().Be(0m);
    }

    private static AccountingEntry Entry(
        long id,
        bool reverse = false,
        bool isStorno = false,
        decimal amount = 100m,
        DateTime? period = null,
        Guid? documentId = null,
        Guid debitDimensions = default,
        Guid creditDimensions = default)
        => new()
        {
            EntryId = id,
            DocumentId = documentId ?? DocumentId,
            Period = period ?? Period,
            Debit = reverse ? Revenue : Cash,
            Credit = reverse ? Cash : Revenue,
            DebitDimensionSetId = debitDimensions,
            CreditDimensionSetId = creditDimensions,
            Amount = amount,
            IsStorno = isStorno
        };
}
