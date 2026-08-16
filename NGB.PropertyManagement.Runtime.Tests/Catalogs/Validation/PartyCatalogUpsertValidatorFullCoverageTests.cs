using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using NGB.Definitions.Catalogs.Validation;
using NGB.PropertyManagement.Runtime.Catalogs.Validation;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Catalogs.Validation;

public sealed class PartyCatalogUpsertValidatorFullCoverageTests
{
    [Fact]
    public async Task Validator_accepts_default_and_each_active_role_combination_case_insensitively()
    {
        var validator = new PartyCatalogUpsertValidator();
        validator.TypeCode.Should().Be(PropertyManagementCodes.Party);

        await validator.ValidateUpsertAsync(Context(PropertyManagementCodes.Party.ToUpperInvariant(),
            new Dictionary<string, object?>()), default);
        await validator.ValidateUpsertAsync(Context(PropertyManagementCodes.Party,
            new Dictionary<string, object?> { ["is_tenant"] = true, ["is_vendor"] = false }), default);
        await validator.ValidateUpsertAsync(Context(PropertyManagementCodes.Party,
            new Dictionary<string, object?> { ["is_tenant"] = false, ["is_vendor"] = true }), default);
        await validator.ValidateUpsertAsync(Context(PropertyManagementCodes.Party,
            new Dictionary<string, object?> { ["is_vendor"] = false }), default);
    }

    [Fact]
    public async Task Validator_rejects_wrong_binding_and_no_active_roles()
    {
        var validator = new PartyCatalogUpsertValidator();
        await ((Func<Task>)(() => validator.ValidateUpsertAsync(Context("wrong", new Dictionary<string, object?>()), default)))
            .Should().ThrowAsync<NgbConfigurationViolationException>();

        await ((Func<Task>)(() => validator.ValidateUpsertAsync(Context(PropertyManagementCodes.Party,
                new Dictionary<string, object?> { ["is_tenant"] = false, ["is_vendor"] = false }), default)))
            .Should().ThrowAsync<PartyValidationException>();

        await ((Func<Task>)(() => validator.ValidateUpsertAsync(Context(PropertyManagementCodes.Party,
                new Dictionary<string, object?> { ["is_tenant"] = false }), default)))
            .Should().ThrowAsync<PartyValidationException>();
    }

    [Fact]
    public void Boolean_parser_covers_native_string_json_null_undefined_and_invalid_values()
    {
        var parser = typeof(PartyCatalogUpsertValidator)
            .GetMethod("TryReadBool", BindingFlags.Static | BindingFlags.NonPublic)!;

        AssertParsed(parser, new Dictionary<string, object?>(), expectedResult: false, expectedValue: null);
        AssertParsed(parser, new Dictionary<string, object?> { ["flag"] = null }, false, null);
        AssertParsed(parser, new Dictionary<string, object?> { ["flag"] = true }, true, true);
        AssertParsed(parser, new Dictionary<string, object?> { ["flag"] = false }, true, false);
        AssertParsed(parser, new Dictionary<string, object?> { ["flag"] = "true" }, true, true);
        AssertParsed(parser, new Dictionary<string, object?> { ["flag"] = "false" }, true, false);
        AssertParsed(parser, new Dictionary<string, object?> { ["flag"] = "invalid" }, false, null);
        AssertParsed(parser, new Dictionary<string, object?> { ["flag"] = JsonSerializer.SerializeToElement(true) }, true, true);
        AssertParsed(parser, new Dictionary<string, object?> { ["flag"] = JsonSerializer.SerializeToElement(false) }, true, false);
        AssertParsed(parser, new Dictionary<string, object?> { ["flag"] = JsonSerializer.SerializeToElement("true") }, true, true);
        AssertParsed(parser, new Dictionary<string, object?> { ["flag"] = JsonSerializer.SerializeToElement("invalid") }, false, null);
        AssertParsed(parser, new Dictionary<string, object?> { ["flag"] = JsonSerializer.SerializeToElement<object?>(null) }, true, null);
        AssertParsed(parser, new Dictionary<string, object?> { ["flag"] = default(JsonElement) }, true, null);
        AssertParsed(parser, new Dictionary<string, object?> { ["flag"] = 1 }, false, null);
    }

    private static void AssertParsed(
        MethodInfo parser,
        IReadOnlyDictionary<string, object?> fields,
        bool expectedResult,
        bool? expectedValue)
    {
        object?[] arguments = [fields, "flag", null];
        parser.Invoke(null, arguments).Should().Be(expectedResult);
        arguments[2].Should().Be(expectedValue);
    }

    private static CatalogUpsertValidationContext Context(
        string typeCode,
        IReadOnlyDictionary<string, object?> fields)
        => new(typeCode, Guid.CreateVersion7(), IsCreate: true, fields);
}
