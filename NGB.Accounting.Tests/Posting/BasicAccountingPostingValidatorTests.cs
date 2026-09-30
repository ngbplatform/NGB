using FluentAssertions;
using System.Reflection;
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

    [Fact]
    public void Validate_Required_dimension_after_an_optional_dimension_is_accepted()
    {
        var optionalRule = Rule("Department", required: false, ordinal: 10);
        var requiredRule = Rule("Warehouse", required: true, ordinal: 20);
        var dimensions = new DimensionBag(
        [
            new DimensionValue(optionalRule.DimensionId, Guid.CreateVersion7()),
            new DimensionValue(requiredRule.DimensionId, Guid.CreateVersion7())
        ]);
        var entry = CreateEntry(
            Guid.CreateVersion7(),
            new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            CreateAccount("41", [optionalRule, requiredRule]),
            CreateAccount("60"),
            10m,
            debitDimensions: dimensions);

        var act = () => new BasicAccountingPostingValidator().Validate([entry]);

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_NullEntries_Throws()
    {
        var act = () => new BasicAccountingPostingValidator().Validate(null!);

        act.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void Validate_EmptyEntries_Throws()
    {
        var act = () => new BasicAccountingPostingValidator().Validate([]);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*no accounting entries*");
    }

    [Fact]
    public void Validate_FirstPeriodIsNotUtc_Throws()
    {
        var entry = CreateValidEntry();
        SetPeriodBypassingEntryInvariant(entry, new DateTime(2026, 1, 10));

        var act = () => new BasicAccountingPostingValidator().Validate([entry]);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*period must be UTC*");
    }

    [Fact]
    public void Validate_LaterPeriodIsNotUtc_Throws()
    {
        var documentId = Guid.CreateVersion7();
        var first = CreateValidEntry(documentId: documentId);
        var second = CreateValidEntry(documentId: documentId);
        SetPeriodBypassingEntryInvariant(
            second,
            new DateTime(2026, 1, 10, 1, 0, 0, DateTimeKind.Unspecified));

        var act = () => new BasicAccountingPostingValidator().Validate([first, second]);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*period must be UTC*");
    }

    [Fact]
    public void Validate_DifferentUtcDay_Throws()
    {
        var documentId = Guid.CreateVersion7();
        var first = CreateValidEntry(documentId: documentId);
        var second = CreateValidEntry(
            documentId: documentId,
            period: new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc));

        var act = () => new BasicAccountingPostingValidator().Validate([first, second]);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*same UTC day*");
    }

    [Fact]
    public void Validate_DifferentDocumentId_Throws()
    {
        var first = CreateValidEntry(documentId: Guid.CreateVersion7());
        var second = CreateValidEntry(documentId: Guid.CreateVersion7());

        var act = () => new BasicAccountingPostingValidator().Validate([first, second]);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*same DocumentId*");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Validate_NonPositiveAmount_Throws(string value)
    {
        var entry = CreateValidEntry(amount: decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));

        var act = () => new BasicAccountingPostingValidator().Validate([entry]);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*amount must be > 0*");
    }

    [Fact]
    public void Validate_AmountWithMoreThanFourDecimalPlaces_Throws()
    {
        var entry = CreateValidEntry(amount: 1.00001m);

        var act = () => new BasicAccountingPostingValidator().Validate([entry]);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*too many decimal places*");
    }

    [Fact]
    public void Validate_MissingDebit_Throws()
    {
        var valid = CreateValidEntry();
        var entry = new AccountingEntry
        {
            DocumentId = valid.DocumentId,
            Period = valid.Period,
            Debit = null!,
            Credit = valid.Credit,
            Amount = valid.Amount
        };

        var act = () => new BasicAccountingPostingValidator().Validate([entry]);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*Debit account is required*");
    }

    [Fact]
    public void Validate_MissingCredit_Throws()
    {
        var valid = CreateValidEntry();
        var entry = new AccountingEntry
        {
            DocumentId = valid.DocumentId,
            Period = valid.Period,
            Debit = valid.Debit,
            Credit = null!,
            Amount = valid.Amount
        };

        var act = () => new BasicAccountingPostingValidator().Validate([entry]);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*Credit account is required*");
    }

    [Fact]
    public void Validate_SameAccountReferenceOnBothSides_Throws()
    {
        var account = CreateAccount("41");
        var entry = CreateEntry(
            Guid.CreateVersion7(),
            new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            account,
            account,
            10m);

        var act = () => new BasicAccountingPostingValidator().Validate([entry]);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*must be different*");
    }

    [Fact]
    public void Validate_DifferentAccountInstancesWithSameId_Throws()
    {
        var accountId = Guid.CreateVersion7();
        var debit = new Account(accountId, "41", "Debit", AccountType.Asset);
        var credit = new Account(accountId, "42", "Credit", AccountType.Asset);
        var entry = CreateEntry(
            Guid.CreateVersion7(),
            new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            debit,
            credit,
            10m);

        var act = () => new BasicAccountingPostingValidator().Validate([entry]);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*must be different*");
    }

    [Fact]
    public void Validate_UnknownDimensionForAccountWithRules_Throws()
    {
        var optionalRule = Rule("Warehouse", required: false, ordinal: 10);
        var debit = CreateAccount("41", [optionalRule]);
        var credit = CreateAccount("60");
        var unknownDimensions = new DimensionBag(
            [new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7())]);
        var entry = CreateEntry(
            Guid.CreateVersion7(),
            new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            debit,
            credit,
            10m,
            debitDimensions: unknownDimensions);

        var act = () => new BasicAccountingPostingValidator().Validate([entry]);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*does not allow dimension*");
    }

    [Fact]
    public void Validate_NullDimensionBag_IsTreatedAsEmpty()
    {
        var optionalRule = Rule("Warehouse", required: false, ordinal: 10);
        var entry = new AccountingEntry
        {
            DocumentId = Guid.CreateVersion7(),
            Period = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Debit = CreateAccount("41", [optionalRule]),
            Credit = CreateAccount("60"),
            Amount = 10m,
            DebitDimensions = null!
        };

        var act = () => new BasicAccountingPostingValidator().Validate([entry]);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateSide_NullAccount_DefensiveGuardThrows()
    {
        var validateSide = typeof(BasicAccountingPostingValidator)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method => method.Name.Contains("ValidateSide", StringComparison.Ordinal));

        var act = () => validateSide.Invoke(null, ["Debit", null, DimensionBag.Empty, Guid.CreateVersion7()]);

        act.Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeOfType<NgbArgumentRequiredException>();
    }

    private static AccountingEntry CreateValidEntry(
        Guid? documentId = null,
        DateTime? period = null,
        decimal amount = 1m)
        => CreateEntry(
            documentId ?? Guid.CreateVersion7(),
            period ?? new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            CreateAccount("41"),
            CreateAccount("60"),
            amount);

    private static void SetPeriodBypassingEntryInvariant(AccountingEntry entry, DateTime value)
    {
        var field = typeof(AccountingEntry).GetField("_periodUtc", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        field!.SetValue(entry, value);
    }
}
