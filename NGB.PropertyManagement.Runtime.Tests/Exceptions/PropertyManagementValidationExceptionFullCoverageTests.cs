using System.Reflection;
using FluentAssertions;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Exceptions;

public sealed class PropertyManagementValidationExceptionFullCoverageTests
{
    private static readonly NullabilityInfoContext Nullability = new();

    [Fact]
    public void Every_public_validation_exception_factory_produces_a_stable_error_contract()
    {
        var assembly = typeof(PropertyValidationException).Assembly;
        var exceptionTypes = assembly.GetTypes()
            .Where(type => type.Namespace == typeof(PropertyValidationException).Namespace)
            .Where(type => !type.IsAbstract && typeof(NgbException).IsAssignableFrom(type))
            .OrderBy(type => type.FullName)
            .ToArray();
        var factoryCount = 0;

        foreach (var exceptionType in exceptionTypes)
        {
            foreach (var constructor in exceptionType.GetConstructors(BindingFlags.Instance | BindingFlags.Public))
            {
                AssertExceptionContract(exceptionType, constructor.Invoke(CreateArguments(constructor.GetParameters(), populated: false)));
                AssertExceptionContract(exceptionType, constructor.Invoke(CreateArguments(constructor.GetParameters(), populated: true)));
            }

            foreach (var factory in exceptionType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(method => exceptionType.IsAssignableFrom(method.ReturnType)))
            {
                factoryCount++;
                AssertExceptionContract(exceptionType, factory.Invoke(null, CreateArguments(factory.GetParameters(), populated: false)));
                AssertExceptionContract(exceptionType, factory.Invoke(null, CreateArguments(factory.GetParameters(), populated: true)));
            }

            foreach (var contextBuilder in exceptionType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                         .Where(method => method.Name is "BuildContext" or "BuildFieldContext")
                         .Where(method => typeof(IEnumerable<KeyValuePair<string, object?>>).IsAssignableFrom(method.ReturnType)))
            {
                contextBuilder.Invoke(null, CreateArguments(contextBuilder.GetParameters(), populated: false))
                    .Should().NotBeNull();
                contextBuilder.Invoke(null, CreateArguments(contextBuilder.GetParameters(), populated: true))
                    .Should().NotBeNull();
            }
        }

        exceptionTypes.Should().NotBeEmpty();
        factoryCount.Should().BeGreaterThan(50);
    }

    [Theory]
    [InlineData("address_line1")]
    [InlineData("address_line2")]
    [InlineData("city")]
    [InlineData("state")]
    [InlineData("zip")]
    [InlineData("unit_no")]
    [InlineData("parent_property_id")]
    [InlineData("kind")]
    [InlineData("custom_field")]
    public void Property_address_factories_cover_every_field_label(string field)
    {
        AssertExceptionContract(typeof(PropertyValidationException), PropertyValidationException.BuildingAddressRequired(field));
        AssertExceptionContract(typeof(PropertyValidationException), PropertyValidationException.UnitAddressNotAllowed(field));
    }

    [Theory]
    [InlineData("fields")]
    [InlineData("amount")]
    public void Batch_missing_field_factories_cover_special_and_regular_messages(string field)
    {
        AssertExceptionContract(typeof(PayablesApplyBatchValidationException),
            PayablesApplyBatchValidationException.PayloadFieldMissing(field));
        AssertExceptionContract(typeof(ReceivablesApplyBatchValidationException),
            ReceivablesApplyBatchValidationException.PayloadFieldMissing(field));
    }

    [Theory]
    [InlineData("credit_document_id")]
    [InlineData("charge_document_id")]
    [InlineData("applied_on_utc")]
    [InlineData("amount")]
    [InlineData("custom_field")]
    public void Batch_invalid_field_factories_cover_every_message_variant(string field)
    {
        AssertExceptionContract(typeof(PayablesApplyBatchValidationException),
            PayablesApplyBatchValidationException.PayloadFieldInvalid(field, "raw"));
        AssertExceptionContract(typeof(ReceivablesApplyBatchValidationException),
            ReceivablesApplyBatchValidationException.PayloadFieldInvalid(field, "raw"));
    }

    [Fact]
    public void Validation_labels_cover_all_aliases_and_unknown_fallback()
    {
        var labelMethod = typeof(PropertyValidationException).Assembly
            .GetType("NGB.PropertyManagement.Runtime.Exceptions.PropertyManagementValidationLabels", throwOnError: true)!
            .GetMethod("Label", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["partyId"] = "Tenant",
            ["party_id"] = "Tenant",
            ["propertyId"] = "Property",
            ["property_id"] = "Property",
            ["leaseId"] = "Lease",
            ["lease_id"] = "Lease",
            ["buildingId"] = "Building",
            ["creditDocumentId"] = "Credit Source",
            ["credit_document_id"] = "Credit Source",
            ["originalPaymentId"] = "Original Payment",
            ["original_payment_id"] = "Original Payment",
            ["chargeDocumentId"] = "Charge",
            ["charge_document_id"] = "Charge",
            ["bankAccountId"] = "Bank Account",
            ["bank_account_id"] = "Bank Account",
            ["applyId"] = "Application",
            ["asOfUtc"] = "As Of",
            ["asOfMonth"] = "As of month",
            ["toMonth"] = "To month",
            ["fromMonthInclusive"] = "From month",
            ["toMonthInclusive"] = "To month",
            ["applied_on_utc"] = "Applied On",
            ["returned_on_utc"] = "Returned On",
            ["credited_on_utc"] = "Credited On",
            ["paid_on_utc"] = "Paid On",
            ["amount"] = "Amount",
            ["last4"] = "Last 4 digits",
            ["gl_account_id"] = "GL Account",
            ["is_default"] = "Default",
            ["maxApplications"] = "Max applications",
            ["limit"] = "Limit",
            ["applies"] = "Applications",
            ["fields"] = "Application details"
        };

        foreach (var pair in expected)
            labelMethod.Invoke(null, [pair.Key]).Should().Be(pair.Value);
        labelMethod.Invoke(null, ["future_field"]).Should().Be("future_field");
    }

    [Fact]
    public void Bulk_create_context_filter_handles_missing_wrong_empty_and_present_errors()
    {
        var filter = typeof(PropertyValidationException).Assembly
            .GetType("NGB.PropertyManagement.Runtime.Exceptions.PropertyBulkCreateUnitsValidationContextExtensions", throwOnError: true)!
            .GetMethod("WithoutEmptyErrors", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;

        var missing = new Dictionary<string, object?>(StringComparer.Ordinal);
        filter.Invoke(null, [missing]).Should().BeSameAs(missing);

        var wrongType = new Dictionary<string, object?>(StringComparer.Ordinal) { ["errors"] = "invalid" };
        filter.Invoke(null, [wrongType]).Should().BeSameAs(wrongType);

        var mixed = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["empty"] = [],
                ["present"] = ["message"]
            }
        };
        filter.Invoke(null, [mixed]).Should().BeSameAs(mixed);
        mixed["errors"].Should().BeEquivalentTo(new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["present"] = ["message"]
        });
    }

    private static void AssertExceptionContract(Type expectedType, object? candidate)
    {
        var exception = candidate.Should().BeAssignableTo<NgbException>().Subject;
        exception.Should().BeOfType(expectedType);
        exception.Message.Should().NotBeNullOrWhiteSpace();
        exception.ErrorCode.Should().NotBeNullOrWhiteSpace();
        exception.Context.Should().NotBeNull();
    }

    private static object?[] CreateArguments(IReadOnlyList<ParameterInfo> parameters, bool populated)
        => parameters.Select(parameter => CreateValue(parameter, populated)).ToArray();

    private static object? CreateValue(ParameterInfo parameter, bool populated)
    {
        var type = parameter.ParameterType;
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType is not null)
            return populated ? CreateNonNullValue(nullableType, parameter.Name) : null;

        if (!type.IsValueType && Nullability.Create(parameter).ReadState == NullabilityState.Nullable)
            return populated ? CreateNonNullValue(type, parameter.Name) : null;

        return CreateNonNullValue(type, parameter.Name);
    }

    private static object? CreateNonNullValue(Type type, string? parameterName)
    {
        if (type == typeof(string)) return parameterName is "errorCode" ? "pm.validation.test" : "value";
        if (type == typeof(Guid)) return Guid.CreateVersion7();
        if (type == typeof(DateOnly)) return new DateOnly(2026, 8, 16);
        if (type == typeof(DateTime)) return new DateTime(2026, 8, 16, 12, 0, 0, DateTimeKind.Utc);
        if (type == typeof(decimal)) return 1m;
        if (type == typeof(int)) return 1;
        if (type == typeof(long)) return 1L;
        if (type == typeof(bool)) return true;
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0);
        if (typeof(Exception).IsAssignableFrom(type)) return new InvalidOperationException("inner");

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();
            if (definition == typeof(Dictionary<,>) || ImplementsGeneric(type, typeof(IReadOnlyDictionary<,>)))
            {
                if (arguments[0] == typeof(string) && arguments[1] == typeof(string[]))
                    return new Dictionary<string, string[]>(StringComparer.Ordinal) { ["field"] = ["message"] };
                if (arguments[0] == typeof(string) && arguments[1] == typeof(object))
                    return new Dictionary<string, object?>(StringComparer.Ordinal) { ["field"] = "value" };
                return Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(arguments));
            }

            if (ImplementsGeneric(type, typeof(IEnumerable<>)))
            {
                var array = Array.CreateInstance(arguments[0], 1);
                array.SetValue(CreateSimpleValue(arguments[0]), 0);
                return array;
            }
        }

        return Activator.CreateInstance(type);
    }

    private static object? CreateSimpleValue(Type type)
        => type == typeof(Guid) ? Guid.CreateVersion7()
            : type == typeof(string) ? "value"
            : type.IsEnum ? Enum.GetValues(type).GetValue(0)
            : Activator.CreateInstance(type);

    private static bool ImplementsGeneric(Type type, Type genericDefinition)
        => type.IsGenericType && type.GetGenericTypeDefinition() == genericDefinition
           || type.GetInterfaces().Any(candidate =>
               candidate.IsGenericType && candidate.GetGenericTypeDefinition() == genericDefinition);
}
