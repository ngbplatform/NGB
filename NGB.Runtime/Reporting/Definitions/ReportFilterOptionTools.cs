using System.ComponentModel.DataAnnotations;
using System.Reflection;
using NGB.Contracts.Reporting;

namespace NGB.Runtime.Reporting.Definitions;

public static class ReportFilterOptionTools
{
    public static ReportFilterOptionDto[] ToReportFilterOptions<TEnum>()
        where TEnum : struct, Enum
    {
        return Enum.GetValues<TEnum>()
            .Select(value =>
            {
                var name = value.ToString();
                var label = typeof(TEnum)
                    .GetMember(name, BindingFlags.Public | BindingFlags.Static)
                    .Single()
                    .GetCustomAttribute<DisplayAttribute>()
                    ?.GetName();

                return new ReportFilterOptionDto(name, string.IsNullOrWhiteSpace(label) ? name : label);
            })
            .ToArray();
    }
}
