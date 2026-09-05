using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Catalogs.Exceptions;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Policy;

public sealed class PropertyManagementPolicyReadersFullCoverageTests
{
    [Fact]
    public async Task Accounting_policy_reader_uses_posting_read_cache_when_available()
    {
        var cache = new Mock<IDocumentPostingReadCache>(MockBehavior.Strict);
        cache.Setup(x => x.GetOrAddAsync<PropertyManagementAccountingPolicy>(
                "policy:property-management",
                It.IsAny<Func<CancellationToken, Task<PropertyManagementAccountingPolicy>>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string _, Func<CancellationToken, Task<PropertyManagementAccountingPolicy>> factory, CancellationToken ct) => factory(ct));
        var sut = new PropertyManagementAccountingPolicyReader(
            CatalogPage(Page([PolicyItem(Guid.CreateVersion7())])),
            cache.Object);

        var result = await sut.GetRequiredAsync();

        result.Should().NotBeNull();
        cache.VerifyAll();
    }

    [Fact]
    public async Task Party_reader_maps_boolean_literals_and_strings_and_handles_not_found()
    {
        var id = Guid.CreateVersion7();
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.SetupSequence(x => x.GetByIdAsync(PropertyManagementCodes.Party, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Item(id, Fields(("is_tenant", true), ("is_vendor", false)), deleted: true))
            .ReturnsAsync(Item(id, Fields(("is_tenant", "false"), ("is_vendor", "true"))))
            .ThrowsAsync(new CatalogNotFoundException(id));
        var reader = new PropertyManagementPartyReader(catalogs.Object);

        var literal = await reader.TryGetAsync(id);
        literal.Should().Be(new PropertyManagementParty(id, "Item", true, false, true));

        var text = await reader.GetRequiredAsync(id);
        text.Should().Be(new PropertyManagementParty(id, "Item", false, true, false));

        (await reader.TryGetAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task Party_reader_batch_returns_empty_without_query_for_only_empty_ids()
    {
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        var reader = new PropertyManagementPartyReader(catalogs.Object);

        var result = await reader.GetByIdsAsync([Guid.Empty, Guid.Empty]);

        result.Should().BeEmpty();
        catalogs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Party_reader_rejects_missing_and_malformed_role_fields()
    {
        var id = Guid.CreateVersion7();
        foreach (var fields in new IReadOnlyDictionary<string, JsonElement>?[]
                 {
                     null,
                     new Dictionary<string, JsonElement>(),
                     Fields(("is_tenant", 1), ("is_vendor", true)),
                     Fields(("is_tenant", "invalid"), ("is_vendor", true)),
                     Fields(("is_tenant", true), ("is_vendor", new { value = true }))
                 })
        {
            var reader = new PropertyManagementPartyReader(CatalogById(Item(id, fields)));
            await ((Func<Task>)(() => reader.TryGetAsync(id)))
                .Should().ThrowAsync<NgbConfigurationViolationException>();
        }
    }

    [Fact]
    public async Task Party_reader_required_path_reports_missing_party()
    {
        var id = Guid.CreateVersion7();
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Party, id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CatalogNotFoundException(id));

        await ((Func<Task>)(() => new PropertyManagementPartyReader(catalogs.Object).GetRequiredAsync(id)))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task Bank_account_reader_maps_scalar_and_enriched_guids_and_all_boolean_forms()
    {
        var id = Guid.CreateVersion7();
        var glId = Guid.CreateVersion7();
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.SetupSequence(x => x.GetByIdAsync(PropertyManagementCodes.BankAccount, id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Item(id, Fields(("gl_account_id", glId.ToString()), ("is_default", true)), deleted: true))
            .ReturnsAsync(Item(id, Fields(("gl_account_id", new { id = glId }), ("is_default", "false"))))
            .ReturnsAsync(Item(id, Fields(("gl_account_id", glId), ("is_default", false))))
            .ReturnsAsync(Item(id, Fields(("gl_account_id", glId), ("is_default", "true"))));
        var reader = new PropertyManagementBankAccountReader(catalogs.Object);

        (await reader.TryGetAsync(id)).Should().Be(new PropertyManagementBankAccount(id, "Item", glId, true, true));
        (await reader.TryGetAsync(id)).Should().Be(new PropertyManagementBankAccount(id, "Item", glId, false, false));
        (await reader.TryGetAsync(id)).Should().Be(new PropertyManagementBankAccount(id, "Item", glId, false, false));
        (await reader.GetRequiredAsync(id)).Should().Be(new PropertyManagementBankAccount(id, "Item", glId, true, false));
    }

    [Fact]
    public async Task Bank_account_reader_handles_not_found_and_required_path()
    {
        var id = Guid.CreateVersion7();
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.SetupSequence(x => x.GetByIdAsync(PropertyManagementCodes.BankAccount, id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CatalogNotFoundException(id))
            .ThrowsAsync(new CatalogNotFoundException(id));
        var reader = new PropertyManagementBankAccountReader(catalogs.Object);

        (await reader.TryGetAsync(id)).Should().BeNull();
        await ((Func<Task>)(() => reader.GetRequiredAsync(id)))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task Bank_account_reader_rejects_missing_and_malformed_fields()
    {
        var id = Guid.CreateVersion7();
        var glId = Guid.CreateVersion7();
        foreach (var fields in new IReadOnlyDictionary<string, JsonElement>?[]
                 {
                     null,
                     new Dictionary<string, JsonElement>(),
                     Fields(("gl_account_id", "invalid"), ("is_default", true)),
                     Fields(("gl_account_id", glId)),
                     Fields(("gl_account_id", glId), ("is_default", 1)),
                     Fields(("gl_account_id", glId), ("is_default", "invalid"))
                 })
        {
            var reader = new PropertyManagementBankAccountReader(CatalogById(Item(id, fields)));
            await ((Func<Task>)(() => reader.TryGetAsync(id)))
                .Should().ThrowAsync<NgbConfigurationViolationException>();
        }
    }

    [Fact]
    public async Task Default_bank_account_reader_handles_zero_multiple_and_single_results()
    {
        var id = Guid.CreateVersion7();
        var glId = Guid.CreateVersion7();
        var item = Item(id, Fields(("gl_account_id", glId), ("is_default", true)));
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.SetupSequence(x => x.GetPageAsync(
                PropertyManagementCodes.BankAccount,
                It.IsAny<PageRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Page([]))
            .ReturnsAsync(Page([item, item with { Id = Guid.CreateVersion7() }]))
            .ReturnsAsync(Page([item]));
        var reader = new PropertyManagementBankAccountReader(catalogs.Object);

        (await reader.TryGetDefaultAsync()).Should().BeNull();
        await ((Func<Task>)(() => reader.TryGetDefaultAsync()))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
        (await reader.TryGetDefaultAsync()).Should().Be(new PropertyManagementBankAccount(id, "Item", glId, true, false));
    }

    [Fact]
    public async Task Accounting_policy_reader_rejects_missing_duplicate_missing_field_and_invalid_guid()
    {
        var id = Guid.CreateVersion7();
        await AssertPolicyInvalid(Page([]));
        await AssertPolicyInvalid(Page([PolicyItem(id), PolicyItem(Guid.CreateVersion7())]));
        await AssertPolicyInvalid(Page([Item(id, null)]));
        await AssertPolicyInvalid(Page([Item(id, new Dictionary<string, JsonElement>())]));

        var malformed = PolicyFields();
        malformed["cash_account_id"] = JsonSerializer.SerializeToElement("invalid");
        await AssertPolicyInvalid(Page([Item(id, malformed)]));
    }

    [Fact]
    public async Task Accounting_policy_reader_maps_every_required_reference_shape()
    {
        var id = Guid.CreateVersion7();
        var fields = PolicyFields();
        var cashId = fields["cash_account_id"].GetGuid();
        var arId = fields["ar_tenants_account_id"].GetGuid();
        fields["cash_account_id"] = JsonSerializer.SerializeToElement(new { id = cashId });
        fields["ar_tenants_account_id"] = JsonSerializer.SerializeToElement(arId.ToString());

        var result = await new PropertyManagementAccountingPolicyReader(CatalogPage(Page([Item(id, fields)])))
            .GetRequiredAsync();

        result.PolicyId.Should().Be(id);
        result.CashAccountId.Should().Be(cashId);
        result.AccountsReceivableTenantsAccountId.Should().Be(arId);
        result.AccountsPayableVendorsAccountId.Should().NotBeEmpty();
        result.RentalIncomeAccountId.Should().NotBeEmpty();
        result.LateFeeIncomeAccountId.Should().NotBeEmpty();
        result.TenantBalancesOperationalRegisterId.Should().NotBeEmpty();
        result.ReceivablesOpenItemsOperationalRegisterId.Should().NotBeEmpty();
        result.PayablesOpenItemsOperationalRegisterId.Should().NotBeEmpty();
    }

    private static async Task AssertPolicyInvalid(PageResponseDto<CatalogItemDto> page)
        => await ((Func<Task>)(() => new PropertyManagementAccountingPolicyReader(CatalogPage(page)).GetRequiredAsync()))
            .Should().ThrowAsync<NgbConfigurationViolationException>();

    private static ICatalogService CatalogById(CatalogItemDto item)
    {
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetByIdAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);
        return catalogs.Object;
    }

    private static ICatalogService CatalogPage(PageResponseDto<CatalogItemDto> page)
    {
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetPageAsync(It.IsAny<string>(), It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);
        return catalogs.Object;
    }

    private static CatalogItemDto PolicyItem(Guid id) => Item(id, PolicyFields());

    private static Dictionary<string, JsonElement> PolicyFields()
        => new(StringComparer.Ordinal)
        {
            ["cash_account_id"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()),
            ["ar_tenants_account_id"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()),
            ["ap_vendors_account_id"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()),
            ["rent_income_account_id"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()),
            ["late_fee_income_account_id"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()),
            ["tenant_balances_register_id"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()),
            ["receivables_open_items_register_id"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7()),
            ["payables_open_items_register_id"] = JsonSerializer.SerializeToElement(Guid.CreateVersion7())
        };

    private static Dictionary<string, JsonElement> Fields(params (string Key, object? Value)[] values)
        => values.ToDictionary(x => x.Key, x => JsonSerializer.SerializeToElement(x.Value), StringComparer.Ordinal);

    private static CatalogItemDto Item(
        Guid id,
        IReadOnlyDictionary<string, JsonElement>? fields,
        bool deleted = false)
        => new(id, "Item", new RecordPayload(fields), IsMarkedForDeletion: false, IsDeleted: deleted);

    private static PageResponseDto<CatalogItemDto> Page(IReadOnlyList<CatalogItemDto> items)
        => new(items, Offset: 0, Limit: 2, Total: items.Count);
}
