using System.Globalization;
using System.Text.Json;
using NGB.Contracts.Reporting;

namespace NGB.Runtime.Reporting.Rendering;

internal sealed class ReportCellFormatter
{
    private const string TimeFormat = "hh:mm:ss tt";
    private const string DefaultDateTimeFormat = "MM/dd/yyyy hh:mm:ss tt";
    private const string DayFormat = "MM/dd/yyyy";
    private const string WeekFormat = "Week of {0:MM/dd/yyyy}";
    private const string MonthFormat = "MMMM yyyy";
    private const string QuarterFormat = "Q{0} {1:yyyy}";
    private const string YearFormat = "yyyy"; 
    
    private static int GetQuarter(int month) => (month - 1) / 3 + 1;
    
    public ReportCellDto BuildCell(
        object? value,
        ReportSheetColumnDto column,
        string? styleKey = null,
        string? semanticRole = null,
        ReportCellActionDto? action = null)
        => new(
            Value: ToJsonElement(value),
            Display: FormatDisplay(value, ResolveTimeGrain(column.Code)),
            ValueType: column.DataType,
            StyleKey: styleKey,
            SemanticRole: semanticRole ?? column.SemanticRole,
            Action: action);

    public ReportCellDto BuildBlankCell(
        ReportSheetColumnDto column,
        string? styleKey = null,
        string? semanticRole = null)
        => new(
            Value: null,
            Display: null,
            ValueType: column.DataType,
            StyleKey: styleKey,
            SemanticRole: semanticRole ?? column.SemanticRole);

    public ReportCellDto BuildLabelCell(
        string label,
        string? styleKey = null,
        string? semanticRole = null,
        int colSpan = 1,
        int rowSpan = 1,
        ReportCellActionDto? action = null)
        => new(
            Value: ToJsonElement(label),
            Display: label,
            ValueType: "string",
            ColSpan: colSpan,
            RowSpan: rowSpan,
            StyleKey: styleKey,
            SemanticRole: semanticRole,
            Action: action);

    public string? FormatDisplay(object? value, ReportTimeGrain? timeGrain = null)
        => value switch
        {
            null => null,
            DateTime dt => FormatTemporalDisplay(dt, timeGrain),
            DateTimeOffset dto => FormatTemporalDisplay(dto, timeGrain),
            DateOnly date => FormatTemporalDisplay(date, timeGrain),
            TimeOnly time => time.ToString(TimeFormat, CultureInfo.InvariantCulture),
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            double dbl => dbl.ToString(CultureInfo.InvariantCulture),
            float flt => flt.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)
        };

    public string FormatGroupLabel(object? value, ReportTimeGrain? timeGrain = null)
    {
        var display = FormatDisplay(value, timeGrain);
        return string.IsNullOrWhiteSpace(display)
            ? "(blank)"
            : display!;
    }
    
    private static string FormatTemporalDisplay(DateTime value, ReportTimeGrain? timeGrain)
        => timeGrain switch
        {
            ReportTimeGrain.Day => value.ToString(DayFormat, CultureInfo.InvariantCulture),
            ReportTimeGrain.Week => string.Format(WeekFormat, value),
            ReportTimeGrain.Month => value.ToString(MonthFormat, CultureInfo.InvariantCulture),
            ReportTimeGrain.Quarter => string.Format(QuarterFormat, GetQuarter(value.Month), value),
            ReportTimeGrain.Year => value.ToString(YearFormat, CultureInfo.InvariantCulture),
            _ => value.ToString(DefaultDateTimeFormat, CultureInfo.InvariantCulture)
        };

    private static string FormatTemporalDisplay(DateTimeOffset value, ReportTimeGrain? timeGrain)
        => timeGrain switch
        {
            ReportTimeGrain.Day => value.ToString(DayFormat, CultureInfo.InvariantCulture),
            ReportTimeGrain.Week => string.Format(WeekFormat, value),
            ReportTimeGrain.Month => value.ToString(MonthFormat, CultureInfo.InvariantCulture),
            ReportTimeGrain.Quarter => string.Format(QuarterFormat, GetQuarter(value.Month), value),
            ReportTimeGrain.Year => value.ToString(YearFormat, CultureInfo.InvariantCulture),
            _ => value.ToString(DefaultDateTimeFormat, CultureInfo.InvariantCulture)
        };

    private static string FormatTemporalDisplay(DateOnly value, ReportTimeGrain? timeGrain)
        => timeGrain switch
        {
            ReportTimeGrain.Week => string.Format(WeekFormat, value),
            ReportTimeGrain.Month => value.ToString(MonthFormat, CultureInfo.InvariantCulture),
            ReportTimeGrain.Quarter => string.Format(QuarterFormat, GetQuarter(value.Month), value),
            ReportTimeGrain.Year => value.ToString(YearFormat, CultureInfo.InvariantCulture),
            _ => value.ToString(DayFormat, CultureInfo.InvariantCulture)
        };

    private static ReportTimeGrain? ResolveTimeGrain(string? columnCode)
    {
        var code = columnCode?.Trim();
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var suffixIndex = code.LastIndexOf("__", StringComparison.Ordinal);
        if (suffixIndex < 0 || suffixIndex >= code.Length - 2)
            return null;

        var suffix = code[(suffixIndex + 2)..];
        return suffix.ToLowerInvariant() switch
        {
            "day" => ReportTimeGrain.Day,
            "week" => ReportTimeGrain.Week,
            "month" => ReportTimeGrain.Month,
            "quarter" => ReportTimeGrain.Quarter,
            "year" => ReportTimeGrain.Year,
            _ => null
        };
    }

    private static JsonElement? ToJsonElement(object? value)
    {
        if (value is null)
            return null;

        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, value.GetType());
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }
}
