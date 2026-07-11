using NGB.Definitions.Documents.Numbering;

namespace NGB.CRM.Documents.Numbering;

public sealed class CrmLeadIntakeNumberingPolicy : CrmNumberingPolicy
{
    public override string TypeCode => CrmCodes.LeadIntake;
}

public sealed class CrmLeadQualificationNumberingPolicy : CrmNumberingPolicy
{
    public override string TypeCode => CrmCodes.LeadQualification;
}

public sealed class CrmLeadConversionNumberingPolicy : CrmNumberingPolicy
{
    public override string TypeCode => CrmCodes.LeadConversion;
}

public sealed class CrmOpportunityUpdateNumberingPolicy : CrmNumberingPolicy
{
    public override string TypeCode => CrmCodes.OpportunityUpdate;
}

public sealed class CrmQuoteNumberingPolicy : CrmNumberingPolicy
{
    public override string TypeCode => CrmCodes.Quote;
}

public sealed class CrmActivityLogNumberingPolicy : CrmNumberingPolicy
{
    public override string TypeCode => CrmCodes.ActivityLog;
}

public abstract class CrmNumberingPolicy : IDocumentNumberingPolicy
{
    public abstract string TypeCode { get; }
    public bool EnsureNumberOnCreateDraft => true;
    public bool EnsureNumberOnPost => false;
}
