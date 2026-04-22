using System.ComponentModel.DataAnnotations;

namespace NGB.AgencyBilling.Enums;

public enum AgencyBillingClientStatus
{
    [Display(Name = "Active")]
    Active = 1,

    [Display(Name = "On Hold")]
    OnHold = 2,

    [Display(Name = "Inactive")]
    Inactive = 3,
}

public enum AgencyBillingTeamMemberType
{
    [Display(Name = "Employee")]
    Employee = 1,

    [Display(Name = "Contractor")]
    Contractor = 2,
}

public enum AgencyBillingProjectStatus
{
    [Display(Name = "Planned")]
    Planned = 1,

    [Display(Name = "Active")]
    Active = 2,

    [Display(Name = "Completed")]
    Completed = 3,

    [Display(Name = "On Hold")]
    OnHold = 4,
}

public enum AgencyBillingProjectBillingModel
{
    [Display(Name = "Time & Materials")]
    TimeAndMaterials = 1,
}

public enum AgencyBillingServiceItemUnitOfMeasure
{
    [Display(Name = "Hour")]
    Hour = 1,

    [Display(Name = "Day")]
    Day = 2,

    [Display(Name = "Week")]
    Week = 3,

    [Display(Name = "Month")]
    Month = 4,
}

public enum AgencyBillingContractBillingFrequency
{
    [Display(Name = "Manual")]
    Manual = 1,

    [Display(Name = "Weekly")]
    Weekly = 2,

    [Display(Name = "Biweekly")]
    Biweekly = 3,

    [Display(Name = "Monthly")]
    Monthly = 4,
}
