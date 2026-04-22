using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Posting;
using NGB.Accounting.Registers;
using NGB.Core.Dimensions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Accounting.Tests.Posting;

public sealed class AccountingStornoFactoryTests
{
    [Fact]
    public void Create_Null_Throws()
    {
        // Arrange
        AccountingEntry[]? entries = null;

        // Act
        // NOTE: Create() is iterator-based, so we force enumeration to ensure guards run
        // even if the implementation ever moves checks inside the iterator.
        var act = () => AccountingStornoFactory.Create(entries!).ToArray();

        // Assert
        act.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void Create_Default_ReversesEntriesAndMarksStorno()
    {
        // Arrange
        var docId = Guid.CreateVersion7();
        var period = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        var debit = new Account(Guid.CreateVersion7(), "41", "Inventory", AccountType.Asset);
        var credit = new Account(Guid.CreateVersion7(), "60", "AP", AccountType.Liability);

        var debitDims = new DimensionBag([new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7())]);
        var creditDims = new DimensionBag([new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7())]);

        var debitSetId = Guid.CreateVersion7();
        var creditSetId = Guid.CreateVersion7();

        var original = new AccountingEntry
        {
            DocumentId = docId,
            Period = period,
            Debit = debit,
            Credit = credit,
            Amount = 123.45m,
            DebitDimensions = debitDims,
            CreditDimensions = creditDims,
            DebitDimensionSetId = debitSetId,
            CreditDimensionSetId = creditSetId
        };

        // Act
        var storno = AccountingStornoFactory.Create([original]).ToArray();

        // Assert
        storno.Should().HaveCount(1);
        storno[0].DocumentId.Should().Be(docId);
        storno[0].Period.Should().Be(period);
        storno[0].IsStorno.Should().BeTrue();

        storno[0].Debit.Should().BeSameAs(credit);
        storno[0].Credit.Should().BeSameAs(debit);
        storno[0].Amount.Should().Be(original.Amount);

        storno[0].DebitDimensions.Should().BeSameAs(creditDims);
        storno[0].CreditDimensions.Should().BeSameAs(debitDims);

        storno[0].DebitDimensionSetId.Should().Be(creditSetId);
        storno[0].CreditDimensionSetId.Should().Be(debitSetId);
    }

    [Fact]
    public void Create_WithExplicitStornoPeriod_UsesIt()
    {
        // Arrange
        var docId = Guid.CreateVersion7();
        var originalPeriod = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);
        var stornoPeriod = new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc);

        var debit = new Account(Guid.CreateVersion7(), "41", "Inventory", AccountType.Asset);
        var credit = new Account(Guid.CreateVersion7(), "60", "AP", AccountType.Liability);

        var original = new AccountingEntry
        {
            DocumentId = docId,
            Period = originalPeriod,
            Debit = debit,
            Credit = credit,
            Amount = 10m
        };

        // Act
        var storno = AccountingStornoFactory.Create([original], stornoPeriodUtc: stornoPeriod).ToArray();

        // Assert
        storno.Should().HaveCount(1);
        storno[0].Period.Should().Be(stornoPeriod);
        storno[0].IsStorno.Should().BeTrue();
    }
}
