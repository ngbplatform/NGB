using NGB.ReferenceRegisters;

namespace NGB.PostgreSql.ReferenceRegisters;

internal static class ReferenceRegisterRecorderCancellationColumn
{
    internal const string Name = "__is_recorder_cancellation";

    internal static bool IsRequired(ReferenceRegisterPeriodicity periodicity, ReferenceRegisterRecordMode recordMode)
        => periodicity != ReferenceRegisterPeriodicity.NonPeriodic
           && recordMode == ReferenceRegisterRecordMode.SubordinateToRecorder;
}
