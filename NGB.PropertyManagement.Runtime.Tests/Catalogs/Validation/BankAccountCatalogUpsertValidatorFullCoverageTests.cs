using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Dimensions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Catalogs.Universal;
using NGB.PropertyManagement.Runtime.Catalogs.Validation;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Runtime.Accounts;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Catalogs.Validation;

public sealed class BankAccountCatalogUpsertValidatorFullCoverageTests
{
    [Fact]
    public async Task Binding_and_every_last4_shape_are_validated()
    {
        var fixture = new Fixture();
        fixture.Sut.TypeCode.Should().Be(PropertyManagementCodes.BankAccount);
        await AssertThrowsAsync<NgbConfigurationViolationException>(() => fixture.ValidateAsync(
            fixture.ValidFields(), typeCode: "wrong"));

        foreach (var raw in new object?[] { null, "", " ", "123", "12A4" })
        {
            var fields = fixture.ValidFields();
            if (raw is not null)
                fields["last4"] = raw;
            else
                fields.Remove("last4");
            await AssertThrowsAsync<BankAccountValidationException>(() => fixture.ValidateAsync(fields));
        }

        var numeric = fixture.ValidFields();
        numeric["last4"] = 1234;
        await fixture.ValidateAsync(numeric);
    }

    [Fact]
    public async Task Gl_account_reference_accepts_guid_and_string_and_rejects_missing_and_malformed_values()
    {
        var fixture = new Fixture();
        foreach (var raw in new object?[] { null, "invalid", 42 })
        {
            var fields = fixture.ValidFields();
            if (raw is null)
                fields.Remove("gl_account_id");
            else
                fields["gl_account_id"] = raw;
            await AssertThrowsAsync<BankAccountValidationException>(() => fixture.ValidateAsync(fields));
        }

        var text = fixture.ValidFields();
        text["gl_account_id"] = fixture.AccountId.ToString();
        await fixture.ValidateAsync(text);
    }

    [Fact]
    public async Task Gl_account_must_exist_be_active_asset_and_not_require_dimensions()
    {
        var missing = new Fixture(accounts: []);
        await AssertThrowsAsync<BankAccountValidationException>(() => missing.ValidateAsync(missing.ValidFields()));

        var deleted = new Fixture(accounts: [Admin(Account(AccountType.Asset), deleted: true)]);
        await AssertThrowsAsync<BankAccountValidationException>(() => deleted.ValidateAsync(deleted.ValidFields()));

        var inactive = new Fixture(accounts: [Admin(Account(AccountType.Asset), active: false)]);
        await AssertThrowsAsync<BankAccountValidationException>(() => inactive.ValidateAsync(inactive.ValidFields()));

        var wrongType = new Fixture(accounts: [Admin(Account(AccountType.Liability))]);
        await AssertThrowsAsync<BankAccountValidationException>(() => wrongType.ValidateAsync(wrongType.ValidFields()));

        var requiredRule = new AccountDimensionRule(Guid.CreateVersion7(), "pm.property", 1, isRequired: true);
        var dimensioned = new Fixture(accounts: [Admin(Account(AccountType.Asset, [requiredRule]))]);
        await AssertThrowsAsync<BankAccountValidationException>(() => dimensioned.ValidateAsync(dimensioned.ValidFields()));

        var optionalRule = new AccountDimensionRule(Guid.CreateVersion7(), "pm.property", 1, isRequired: false);
        var valid = new Fixture(accounts: [Admin(Account(AccountType.Asset, [optionalRule]))]);
        await valid.ValidateAsync(valid.ValidFields());
    }

    [Fact]
    public async Task Non_default_false_and_invalid_boolean_shapes_do_not_query_existing_defaults()
    {
        var fixture = new Fixture();
        foreach (var raw in new object?[] { null, false, "false", "invalid", 1 })
        {
            var fields = fixture.ValidFields();
            if (raw is null)
                fields.Remove("is_default");
            else
                fields["is_default"] = raw;
            await fixture.ValidateAsync(fields);
        }

        fixture.Reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Default_bank_account_allows_self_and_rejects_another_active_default_while_caching_metadata()
    {
        var fixture = new Fixture();
        fixture.Reader.SetupSequence(x => x.GetPageAsync(
                It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(), 0, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([Row(fixture.CatalogId)])
            .ReturnsAsync([Row(Guid.CreateVersion7())]);

        var boolDefault = fixture.ValidFields();
        boolDefault["is_default"] = true;
        await fixture.ValidateAsync(boolDefault);
        var textDefault = fixture.ValidFields();
        textDefault["is_default"] = "true";
        await fixture.ValidateAsync(textDefault);
        await AssertThrowsAsync<BankAccountValidationException>(() => fixture.ValidateAsync(boolDefault));

        fixture.Types.Verify(x => x.GetRequired(PropertyManagementCodes.BankAccount), Times.Once);
    }

    [Fact]
    public async Task Default_validation_rejects_missing_head_and_empty_display_metadata()
    {
        var noHead = new Fixture(metadata: Metadata(tables: []));
        var fields = noHead.ValidFields();
        fields["is_default"] = true;
        await AssertThrowsAsync<NgbConfigurationViolationException>(() => noHead.ValidateAsync(fields));

        var noDisplay = new Fixture(metadata: Metadata(displayColumn: " "));
        fields = noDisplay.ValidFields();
        fields["is_default"] = true;
        await AssertThrowsAsync<NgbConfigurationViolationException>(() => noDisplay.ValidateAsync(fields));
    }

    private static async Task AssertThrowsAsync<T>(Func<Task> action) where T : Exception
        => await action.Should().ThrowAsync<T>();

    private static ChartOfAccountsAdminItem Admin(Account account, bool active = true, bool deleted = false)
        => new() { Account = account, IsActive = active, IsDeleted = deleted };

    private static Account Account(AccountType type, IReadOnlyList<AccountDimensionRule>? rules = null)
        => new(Fixture.SharedAccountId, "1000", "Bank", type, dimensionRules: rules);

    private static CatalogHeadRow Row(Guid id)
        => new(id, false, "Bank", new Dictionary<string, object?>());

    private static CatalogTypeMetadata Metadata(
        IReadOnlyList<CatalogTableMetadata>? tables = null,
        string displayColumn = "name")
        => new(
            PropertyManagementCodes.BankAccount,
            "Bank Account",
            tables ??
            [
                new CatalogTableMetadata(
                    "cat_pm_bank_account",
                    TableKind.Head,
                    [
                        new CatalogColumnMetadata("catalog_id", ColumnType.Guid),
                        new CatalogColumnMetadata("name", ColumnType.String),
                        new CatalogColumnMetadata("last4", ColumnType.String)
                    ],
                    [])
            ],
            new CatalogPresentationMetadata("cat_pm_bank_account", displayColumn),
            new CatalogMetadataVersion(1, "tests"));

    private sealed class Fixture
    {
        public static readonly Guid SharedAccountId = Guid.Parse("00000000-0000-0000-0000-000000001000");

        public Fixture(
            IReadOnlyList<ChartOfAccountsAdminItem>? accounts = null,
            CatalogTypeMetadata? metadata = null)
        {
            Types.Setup(x => x.GetRequired(PropertyManagementCodes.BankAccount)).Returns(metadata ?? Metadata());
            Coa.Setup(x => x.GetAsync(true, It.IsAny<CancellationToken>()))
                .ReturnsAsync(accounts ?? [Admin(Account(AccountType.Asset))]);
            Sut = new BankAccountCatalogUpsertValidator(Types.Object, Reader.Object, Coa.Object);
        }

        public Guid CatalogId { get; } = Guid.CreateVersion7();
        public Guid AccountId => SharedAccountId;
        public Mock<ICatalogTypeRegistry> Types { get; } = new(MockBehavior.Strict);
        public Mock<ICatalogReader> Reader { get; } = new(MockBehavior.Strict);
        public Mock<IChartOfAccountsAdminService> Coa { get; } = new(MockBehavior.Strict);
        public BankAccountCatalogUpsertValidator Sut { get; }

        public Dictionary<string, object?> ValidFields()
            => new()
            {
                ["last4"] = "1234",
                ["gl_account_id"] = AccountId,
                ["is_default"] = false
            };

        public Task ValidateAsync(IReadOnlyDictionary<string, object?> fields, string? typeCode = null)
            => Sut.ValidateUpsertAsync(new CatalogUpsertValidationContext(
                typeCode ?? PropertyManagementCodes.BankAccount, CatalogId, true, fields), default);
    }
}
