using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Runtime.Documents.Validation;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class PayloadParsingFullCoverageTests
{
    private const string Key = "field";

    [Fact]
    public async Task Every_business_payload_validator_exposes_its_type_and_accepts_incomplete_create_and_empty_update()
    {
        var validators = typeof(RentChargePayloadValidator).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IDocumentDraftPayloadValidator).IsAssignableFrom(type))
            .Where(type => type.Name.EndsWith("PayloadValidator", StringComparison.Ordinal))
            .Where(type => !type.Name.Contains("PropertyMustBeUnit", StringComparison.Ordinal))
            .Where(type => type != typeof(LeasePrimaryPartyPayloadValidator))
            .OrderBy(type => type.FullName)
            .Select(CreateWithoutCallingDependencies)
            .ToArray();

        validators.Should().HaveCountGreaterThan(10);
        foreach (var validator in validators)
        {
            validator.TypeCode.Should().StartWith("pm.");
            await validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), EmptyParts, default);
            await validator.ValidateUpdateDraftPayloadAsync(Guid.CreateVersion7(), new RecordPayload(), EmptyParts, default);
            await validator.ValidateUpdateDraftPayloadAsync(
                Guid.CreateVersion7(),
                new RecordPayload(new Dictionary<string, JsonElement>()),
                EmptyParts,
                default);
        }
    }

    [Fact]
    public void Every_payload_try_get_helper_covers_missing_null_valid_and_malformed_values()
    {
        var helperMethods = typeof(RentChargePayloadValidator).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(RentChargePayloadValidator).Namespace)
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
            .Where(method => method.Name.StartsWith("TryGet", StringComparison.Ordinal))
            .Where(method => method.GetParameters() is [_, _, { IsOut: true }])
            .Where(method => method.Name != "TryGetBoolean")
            .OrderBy(method => method.DeclaringType!.FullName)
            .ThenBy(method => method.Name)
            .ToArray();

        helperMethods.Should().HaveCountGreaterThan(20);
        foreach (var method in helperMethods)
        {
            Invoke(method, null).Result.Should().BeFalse(method.ToString());
            Invoke(method, new Dictionary<string, JsonElement>()).Result.Should().BeFalse(method.ToString());

            var undefined = Invoke(method, Fields(default));
            undefined.Result.Should().BeTrue(method.ToString());
            undefined.Value.Should().BeNull();

            var jsonNull = Invoke(method, Fields(JsonSerializer.SerializeToElement<object?>(null)));
            jsonNull.Result.Should().BeTrue(method.ToString());
            jsonNull.Value.Should().BeNull();

            switch (method.Name)
            {
                case "TryGetGuid":
                    AssertGuidCases(method);
                    break;
                case "TryGetDecimal":
                    AssertDecimalCases(method);
                    break;
                case "TryGetDate":
                case "TryGetDateOnly":
                    AssertDateCases(method);
                    break;
                case "TryGetString":
                    AssertStringCases(method);
                    break;
                default:
                    throw new InvalidOperationException($"Unrecognized payload helper '{method}'.");
            }
        }
    }

    [Fact]
    public void Every_property_reference_extractor_covers_scalar_object_null_and_invalid_json_shapes()
    {
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict).Object;
        var validatorTypes = new[]
        {
            typeof(LeasePropertyMustBeUnitPayloadValidator),
            typeof(LateFeeChargePropertyMustBeUnitPayloadValidator),
            typeof(ReceivableChargePropertyMustBeUnitPayloadValidator),
            typeof(ReceivablePaymentPropertyMustBeUnitPayloadValidator)
        };
        var id = Guid.CreateVersion7();

        foreach (var validatorType in validatorTypes)
        {
            var validator = Activator.CreateInstance(validatorType, readers)!;
            var extract = validatorType.GetMethod("ExtractGuid", BindingFlags.Instance | BindingFlags.NonPublic)!;

            InvokeExtract(extract, validator, default).Should().Be(Guid.Empty);
            InvokeExtract(extract, validator, JsonSerializer.SerializeToElement<object?>(null)).Should().Be(Guid.Empty);
            InvokeExtract(extract, validator, JsonSerializer.SerializeToElement(" ")).Should().Be(Guid.Empty);
            InvokeExtract(extract, validator, JsonSerializer.SerializeToElement(id.ToString())).Should().Be(id);
            InvokeExtract(extract, validator, JsonSerializer.SerializeToElement(new { id })).Should().Be(id);
            InvokeExtract(extract, validator, JsonSerializer.SerializeToElement(new { Id = id })).Should().Be(id);
            InvokeExtract(extract, validator, JsonSerializer.SerializeToElement(new { id = (string?)null })).Should().Be(Guid.Empty);
            InvokeExtract(extract, validator, JsonSerializer.SerializeToElement(new { Id = " " })).Should().Be(Guid.Empty);

            AssertInvalidExtract(extract, validator, JsonSerializer.SerializeToElement("invalid"));
            AssertInvalidExtract(extract, validator, JsonSerializer.SerializeToElement(new { other = id }));
            AssertInvalidExtract(extract, validator, JsonSerializer.SerializeToElement(new { id = "invalid" }));
            AssertInvalidExtract(extract, validator, JsonSerializer.SerializeToElement(new { id = 42 }));
            AssertInvalidExtract(extract, validator, JsonSerializer.SerializeToElement(42));
        }
    }

    private static void AssertGuidCases(MethodInfo method)
    {
        var id = Guid.CreateVersion7();
        var scalar = Invoke(method, Fields(JsonSerializer.SerializeToElement(id.ToString())));
        scalar.Result.Should().BeTrue();
        scalar.Value.Should().Be(id);

        var reference = Invoke(method, Fields(JsonSerializer.SerializeToElement(new { id })));
        reference.Result.Should().BeTrue();
        reference.Value.Should().Be(id);

        Invoke(method, Fields(JsonSerializer.SerializeToElement("invalid"))).Result.Should().BeFalse();
        Invoke(method, Fields(JsonSerializer.SerializeToElement(42))).Result.Should().BeFalse();
    }

    private static void AssertDecimalCases(MethodInfo method)
    {
        var number = Invoke(method, Fields(JsonSerializer.SerializeToElement(12.5m)));
        number.Result.Should().BeTrue();
        number.Value.Should().Be(12.5m);

        var text = Invoke(method, Fields(JsonSerializer.SerializeToElement("12.5")));
        text.Result.Should().BeTrue();
        text.Value.Should().Be(12.5m);

        Invoke(method, Fields(JsonSerializer.SerializeToElement("invalid"))).Result.Should().BeFalse();
        Invoke(method, Fields(JsonSerializer.SerializeToElement(new { value = 1 }))).Result.Should().BeFalse();
    }

    private static void AssertDateCases(MethodInfo method)
    {
        var date = Invoke(method, Fields(JsonSerializer.SerializeToElement("2026-08-16")));
        date.Result.Should().BeTrue();
        date.Value.Should().Be(new DateOnly(2026, 8, 16));

        Invoke(method, Fields(JsonSerializer.SerializeToElement("invalid"))).Result.Should().BeFalse();
        Invoke(method, Fields(JsonSerializer.SerializeToElement(20260816))).Result.Should().BeFalse();
    }

    private static void AssertStringCases(MethodInfo method)
    {
        var text = Invoke(method, Fields(JsonSerializer.SerializeToElement("value")));
        text.Result.Should().BeTrue();
        text.Value.Should().Be("value");

        var number = Invoke(method, Fields(JsonSerializer.SerializeToElement(42)));
        number.Result.Should().BeTrue();
        number.Value.Should().Be("42");
    }

    private static (bool Result, object? Value) Invoke(
        MethodInfo method,
        IReadOnlyDictionary<string, JsonElement>? fields)
    {
        object?[] arguments = [fields, Key, null];
        var result = (bool)method.Invoke(null, arguments)!;
        return (result, arguments[2]);
    }

    private static IReadOnlyDictionary<string, JsonElement> Fields(JsonElement element)
        => new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [Key] = element };

    private static Guid InvokeExtract(MethodInfo method, object target, JsonElement element)
        => (Guid)method.Invoke(target, [element])!;

    private static void AssertInvalidExtract(MethodInfo method, object target, JsonElement element)
    {
        var action = () => method.Invoke(target, [element]);
        action.Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeOfType<DocumentPropertyPayloadValidationException>();
    }

    private static IDocumentDraftPayloadValidator CreateWithoutCallingDependencies(Type type)
    {
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        return (IDocumentDraftPayloadValidator)constructor.Invoke(new object?[constructor.GetParameters().Length]);
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> EmptyParts
        = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.Ordinal);
}
