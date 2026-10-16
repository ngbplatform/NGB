using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace NGB.Mcp;

internal static class McpJson
{
    // Bound recursive validation work and individual values independently of the byte budget.
    internal const int MaxCollectionItems = 1_024;
    internal const int MaxStringLength = 16_384;

    internal static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            MaxDepth = McpReadOptions.MaximumSupportedJsonDepth,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
        };

        options.MakeReadOnly();

        return options;
    }

    internal static JsonElement Schema<T>()
    {
        var schema = Options.GetJsonSchemaAsNode(typeof(T), new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) =>
            {
                if (node is not JsonObject schema || context.PropertyInfo?.AttributeProvider is not { } attributes)
                    return node;

                foreach (var attribute in attributes.GetCustomAttributes(inherit: true))
                {
                    if (attribute is RangeAttribute range)
                    {
                        schema["minimum"] = JsonSerializer.SerializeToNode(range.Minimum);
                        schema["maximum"] = JsonSerializer.SerializeToNode(range.Maximum);
                    }
                    else if (attribute is StringLengthAttribute length)
                    {
                        schema["minLength"] = length.MinimumLength;
                        schema["maxLength"] = length.MaximumLength;
                    }
                }
                return schema;
            }
        });

        return JsonSerializer.SerializeToElement(schema, Options);
    }

    internal static bool IsObjectSchema(JsonElement schema)
        => schema.ValueKind == JsonValueKind.Object
            && schema.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && type.GetString() == "object";

    internal static void Validate(object? value, int depth = 0)
    {
        if (value is null)
            return;

        if (depth > McpReadOptions.MaximumSupportedJsonDepth)
            throw new McpReadException("invalid_input", "The request is nested too deeply.");

        if (value is string text)
        {
            if (text.Length > MaxStringLength)
                throw new McpReadException("invalid_input", "A request value is too long.");

            return;
        }

        if (value.GetType().IsPrimitive || value.GetType().IsEnum
            || value is decimal or Guid or DateTime or DateOnly or JsonElement)
        {
            return;
        }

        Validator.ValidateObject(value, new ValidationContext(value), validateAllProperties: true);

        if (value is IEnumerable sequence)
        {
            var count = 0;
            foreach (var item in sequence)
            {
                if (++count > MaxCollectionItems)
                    throw new McpReadException("invalid_input", "The request contains too many values.");

                Validate(item, depth + 1);
            }

            return;
        }

        foreach (var property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0)
                Validate(property.GetValue(value), depth + 1);
        }
    }
}
