using FluentAssertions;
using NGB.Core.WorkCenter;
using NGB.CRM.Runtime.WorkCenter;

namespace NGB.CRM.Runtime.Tests.WorkCenter;

public sealed class CrmWorkCenterPreferenceDefinitionSourceTests
{
    [Fact]
    public void Exposes_role_scoped_preferences_for_every_CRM_task_and_notification_type()
    {
        var definitions = new CrmWorkCenterPreferenceDefinitionSource().GetDefinitions();

        definitions.Select(static definition => definition.Code).Should().BeEquivalentTo(
        [
            CrmWorkCenterCodes.QualifyLeadTask,
            CrmWorkCenterCodes.ConvertQualifiedLeadTask,
            CrmWorkCenterCodes.CompleteActivityTask,
            CrmWorkCenterCodes.LeadQualified,
            CrmWorkCenterCodes.OpportunityWon
        ]);

        foreach (var definition in definitions)
        {
            definition.DefaultEnabled.Should().BeTrue();
            definition.UserCanDisable.Should().BeTrue();
            definition.IsMandatory.Should().BeFalse();
            definition.SupportedChannels.Should()
                .BeEquivalentTo([NotificationChannel.InApp]);
            definition.ApplicableRoleCodes.Should().NotBeNull();
            definition.ApplicableRoleCodes.Should()
                .BeEquivalentTo(new[] { CrmWorkCenterCodes.SalesRepresentativeRole });
        }

        definitions.Where(static definition => definition.Category == "CRM Tasks")
            .Should().HaveCount(3);
        definitions.Where(static definition => definition.Kind == WorkCenterPreferenceKind.Task)
            .Should().HaveCount(3);
        definitions.Where(static definition => definition.Category == "CRM Notifications")
            .Should().HaveCount(2);
        definitions.Where(static definition => definition.Kind == WorkCenterPreferenceKind.Notification)
            .Should().HaveCount(2);
    }
}
