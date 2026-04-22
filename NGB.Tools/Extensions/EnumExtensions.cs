using System.ComponentModel.DataAnnotations;
using System.Reflection;
using FastEnumUtility;

namespace NGB.Tools.Extensions;

public static class EnumExtensions
{
    public static TAttribute? GetAttribute<TEnum, TAttribute>(TEnum value)
        where TEnum : struct, Enum
        where TAttribute : Attribute
    {
        if (!FastEnum.IsDefined(value))
            return null;

        var member = FastEnum.GetMember(value);
        return member?.FieldInfo.GetCustomAttribute<TAttribute>(inherit: false);
    }
    
    public static string ToCode<TEnum>(this TEnum value)
        where TEnum : struct, Enum
        => value.ToString();
    
    public static string ToDisplay<TEnum>(this TEnum value)
        where TEnum : struct, Enum
    {
        var attr = GetAttribute<TEnum, DisplayAttribute>(value);
        return string.IsNullOrEmpty(attr?.Name)
            ? value.ToString()
            : attr.Name;
    }
}