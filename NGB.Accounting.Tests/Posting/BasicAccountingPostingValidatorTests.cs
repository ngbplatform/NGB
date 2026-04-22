using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Dimensions;
using NGB.Accounting.Posting.Validators;
using NGB.Accounting.Registers;
using NGB.Core.Dimensions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Accounting.Tests.Posting;

public sealed class BasicAccountingPostingValidatorTests
{
    private static Account CreateAccount(string code, IReadOnlyList<AccountDimensionRule>? rules = null)
    {
        return new Account(
            id: null,
            code: code,
            name: "Test",
            type: AccountType.Asset,
            dimensionRules: rules);
    }

    private static AccountDimensionRule Rule(string code, bool required, int ordinal)
    {
        var c = code.Trim();
        var dimId = DeterministicGuid.Create($"Dimension|{NormalizeDimensionCode(c)}");
        return new AccountDimensionRule(dimId, c, ordinal, required);
    }

    private static string NormalizeDimensionCode(string code) => code.Trim().ToLowerInvariant();

    private static AccountingEntry CreateEntry(
        Guid documentId,
        DateTime periodUtc,
        Account debit,
        Account credit,
        decimal amount,
        DimensionBag? debitDimensions = null,
        DimensionBag? creditDimensions = null)
    {
        return new AccountingEntry
        {
            DocumentId = documentId,
            Period = periodUtc,
            Debit = debit,
            Credit = credit,
            Amount = amount,
            DebitDimensions = debitDimensions ?? DimensionBag.Empty,
            CreditDimensions = creditDimensions ?? DimensionBag.Empty,
            DebitDimensionSetId = Guid.Empty,
            CreditDimensionSetId = Guid.Empty,
        };
    }

    [Fact]
    public void Validate_EntryHasDimensionsButAccountDoesNotAllow_Throws()
    {
        // Arrange
        var v = new BasicAccountingPostingValidator();
        var docId = Guid.CreateVersion7();

        var debit = CreateAccount("41");
        var credit = CreateAccount("60");

        var dims = new DimensionBag([new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7())]);

        var e = CreateEntry(
            docId,
            new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            debit,
            credit,
            10,
            debitDimensions: dims);

        // Act
        var act = () => v.Validate(new[] { e });

        // Assert
        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*does not allow dimensions*");
    }

    [Fact]
    public void Validate_EntryMissingRequiredDimension_Throws()
    {
        // Arrange
        var v = new BasicAccountingPostingValidator();
        var docId = Guid.CreateVersion7();

        var requiredRule = Rule("Warehouse", required: true, ordinal: 10);

        var debit = CreateAccount("41", rules: new[] { requiredRule });
        var credit = CreateAccount("60");

        var e = CreateEntry(
            docId,
            new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            debit,
            credit,
            10);

        // Act
        var act = () => v.Validate(new[] { e });

        // Assert
        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*requires dimension*Warehouse*");
    }

    [Fact]
    public void Validate_ValidPosting_DoesNotThrow()
    {
        // Arrange
        var v = new BasicAccountingPostingValidator();
        var docId = Guid.CreateVersion7();

        var requiredRule = Rule("Warehouse", required: true, ordinal: 10);

        var debit = CreateAccount("41", rules: new[] { requiredRule });
        var credit = CreateAccount("60");

        var value = Guid.CreateVersion7();
        var dims = new DimensionBag([new DimensionValue(requiredRule.DimensionId, value)]);

        var e = CreateEntry(
            docId,
            new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            debit,
            credit,
            10,
            debitDimensions: dims);

        // Act
        var act = () => v.Validate(new[] { e });

        // Assert
        act.Should().NotThrow();
    }
}
