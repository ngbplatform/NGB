namespace NGB.Contracts.Reporting;

public static class ReportLayoutLimits
{
    public const int MaxRowGroups = 16;
    public const int MaxColumnGroups = 16;
    public const int MaxMeasures = 64;
    public const int MaxDetailFields = 128;
    public const int MaxSorts = 32;
    public const int MaxFilters = 128;
    public const int MaxParameters = 128;
    public const int MaxValuesPerFilter = 500;
    public const int MaxTotalFilterValues = 2_000;
    public const int MaxExpandedDimensionValues = 5_000;
    public const int MaxParameterValueLength = 4_096;
}
