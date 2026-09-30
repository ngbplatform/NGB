namespace NGB.Contracts.Reporting;

public static class ReportVariantLimits
{
    public const int MaxVisibleVariants = 200;
    public const int MaxVariantsPerScope = 100;
    public const int MaxNameLength = 200;
    public const int MaxVariantCodeLength = 128;
    public const int MaxSerializedPayloadBytes = 256 * 1024;
}
