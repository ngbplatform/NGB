using System.Globalization;
using NGB.Tools.Extensions;

namespace NGB.Metadata.Base;

public sealed record FieldOptionMetadata(string Value, string Label);

public static class FieldOptionMetadataTools
{
    public static IReadOnlyList<FieldOptionMetadata> EnumOptions<TEnum>()
        where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        var options = new FieldOptionMetadata[values.Length];

        for (var index = 0; index < values.Length; index++)
        {
            var value = values[index];
            options[index] = new FieldOptionMetadata(
                Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                value.ToDisplay());
        }

        return options;
    }
}
