using FluentAssertions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Tools.Exceptions;
using NGB.Trade.Runtime.Catalogs.Validation;
using NGB.Trade.Runtime.Exceptions;

namespace NGB.Trade.Runtime.Tests.Catalogs;

public sealed class PartyCatalogUpsertValidatorTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Accepts_customer_vendor_and_combined_roles(bool isCustomer, bool isVendor)
    {
        var validator = new PartyCatalogUpsertValidator();
        await validator.ValidateUpsertAsync(Context(new Dictionary<string, object?>
        {
            ["is_customer"] = isCustomer, ["is_vendor"] = isVendor
        }) with { TypeCode = TradeCodes.Party.ToUpperInvariant() }, default);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rejects_no_roles_for_create_and_update_with_errors_on_both_fields(bool isCreate)
    {
        var context = Context(new Dictionary<string, object?>
        {
            ["is_customer"] = false, ["is_vendor"] = false
        }) with { IsCreate = isCreate };
        var action = () => new PartyCatalogUpsertValidator().ValidateUpsertAsync(context, default);

        var error = (await action.Should().ThrowAsync<PartyRoleRequiredException>()).Which;
        error.ErrorCode.Should().Be("trd.validation.party.role_required");
        error.Kind.Should().Be(NgbErrorKind.Validation);
        error.Context["catalogId"].Should().Be(context.CatalogId);
        var fields = (IReadOnlyDictionary<string, string[]>)error.Context["errors"]!;
        fields.Keys.Should().BeEquivalentTo("is_customer", "is_vendor");
        fields.Values.Should().OnlyContain(messages => messages.Single() == error.Message);
    }

    [Fact]
    public async Task Missing_roles_do_not_implicitly_assign_a_customer_or_vendor_role()
    {
        var action = () => new PartyCatalogUpsertValidator().ValidateUpsertAsync(Context(new Dictionary<string, object?>()), default);
        await action.Should().ThrowAsync<PartyRoleRequiredException>();
    }

    [Theory]
    [InlineData("is_customer", null)]
    [InlineData("is_customer", "true")]
    [InlineData("is_customer", 1)]
    [InlineData("is_vendor", null)]
    [InlineData("is_vendor", "true")]
    [InlineData("is_vendor", 1)]
    public async Task Non_boolean_values_do_not_enable_a_role(string field, object? value)
    {
        var fields = new Dictionary<string, object?>
        {
            ["is_customer"] = false, ["is_vendor"] = false
        };
        fields[field] = value;
        var context = Context(fields);
        var action = () => new PartyCatalogUpsertValidator().ValidateUpsertAsync(context, default);

        var error = (await action.Should().ThrowAsync<PartyRoleRequiredException>()).Which;
        error.Context["catalogId"].Should().Be(context.CatalogId);
    }

    [Theory]
    [InlineData("is_customer")]
    [InlineData("is_vendor")]
    public async Task One_explicit_role_is_sufficient_when_the_other_is_missing(string field)
    {
        var action = () => new PartyCatalogUpsertValidator().ValidateUpsertAsync(
            Context(new Dictionary<string, object?> { [field] = true }), default);

        await action.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Rejects_wrong_catalog_binding()
    {
        var action = () => new PartyCatalogUpsertValidator().ValidateUpsertAsync(
            Context(new Dictionary<string, object?>()) with { TypeCode = TradeCodes.Item }, default);
        await action.Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    private static CatalogUpsertValidationContext Context(IReadOnlyDictionary<string, object?> fields)
        => new(TradeCodes.Party, Guid.NewGuid(), IsCreate: true, fields);
}
