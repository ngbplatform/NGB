using System.Globalization;
using NGB.Tools.Extensions;

namespace NGB.Metadata.Base;

public sealed record FieldOptionMetadata(string Value, string Label);

public static class FieldOptionMetadataTools
{
    public static IReadOnlyList<FieldOptionMetadata> EnumOptions<TEnum>()
        where TEnum : struct, Enum
    {
        return Enum.GetValues<TEnum>()
            .Select(value => new FieldOptionMetadata(
                Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                value.ToDisplay()))
            .ToArray();
    }
}
