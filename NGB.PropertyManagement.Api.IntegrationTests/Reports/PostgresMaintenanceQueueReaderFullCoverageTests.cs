using FluentAssertions;
using NGB.PropertyManagement.PostgreSql.Reporting;
using NGB.PropertyManagement.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

public sealed class PostgresMaintenanceQueueReaderFullCoverageTests
{
    [Fact]
    public async Task Optional_filters_accept_null_and_reject_empty_identifiers_before_database_access()
    {
        var sut = new PostgresMaintenanceQueueReader(null!);

        await sut.ValidateBuildingFilterAsync(null, default);
        await sut.ValidatePropertyFilterAsync(null, default);
        await sut.ValidateCategoryFilterAsync(null, default);
        await sut.ValidateAssignedPartyFilterAsync(null, default);

        Func<Task> building = () => sut.ValidateBuildingFilterAsync(Guid.Empty, default);
        Func<Task> property = () => sut.ValidatePropertyFilterAsync(Guid.Empty, default);
        Func<Task> category = () => sut.ValidateCategoryFilterAsync(Guid.Empty, default);
        Func<Task> assignedParty = () => sut.ValidateAssignedPartyFilterAsync(Guid.Empty, default);

        (await building.Should().ThrowAsync<NgbArgumentInvalidException>()).Which.ParamName.Should().Be("buildingId");
        (await property.Should().ThrowAsync<NgbArgumentInvalidException>()).Which.ParamName.Should().Be("propertyId");
        (await category.Should().ThrowAsync<NgbArgumentInvalidException>()).Which.ParamName.Should().Be("categoryId");
        (await assignedParty.Should().ThrowAsync<NgbArgumentInvalidException>()).Which.ParamName.Should().Be("assignedPartyId");
    }

    [Theory]
    [InlineData("Requested", MaintenanceQueueState.Requested)]
    [InlineData("WorkOrdered", MaintenanceQueueState.WorkOrdered)]
    [InlineData("Overdue", MaintenanceQueueState.Overdue)]
    public void Row_mapping_maps_every_queue_state_and_preserves_optional_fields(
        string rawState,
        MaintenanceQueueState expectedState)
    {
        var requestId = Guid.NewGuid();
        var buildingId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var requestedById = Guid.NewGuid();
        Guid? workOrderId = expectedState == MaintenanceQueueState.Requested ? null : Guid.NewGuid();
        Guid? assignedPartyId = expectedState == MaintenanceQueueState.Requested ? null : Guid.NewGuid();
        DateOnly? dueBy = expectedState == MaintenanceQueueState.Requested ? null : new DateOnly(2026, 8, 25);
        var source = new PostgresMaintenanceQueueReader.PageRow(
            requestId,
            "MR-1",
            "Repair sink",
            new DateOnly(2026, 8, 20),
            2,
            buildingId,
            "Building",
            propertyId,
            "Unit 1",
            categoryId,
            "Plumbing",
            "High",
            requestedById,
            "Tenant",
            workOrderId,
            workOrderId is null ? null : "WO-1",
            assignedPartyId,
            assignedPartyId is null ? null : "Technician",
            dueBy,
            rawState);

        var result = PostgresMaintenanceQueueReader.MapRow(source);

        result.Should().BeEquivalentTo(new MaintenanceQueueRow(
            requestId,
            "MR-1",
            "Repair sink",
            new DateOnly(2026, 8, 20),
            2,
            buildingId,
            "Building",
            propertyId,
            "Unit 1",
            categoryId,
            "Plumbing",
            "High",
            requestedById,
            "Tenant",
            workOrderId,
            workOrderId is null ? null : "WO-1",
            assignedPartyId,
            assignedPartyId is null ? null : "Technician",
            dueBy,
            expectedState));
    }

    [Fact]
    public void Row_mapping_rejects_unknown_database_state_with_diagnostic_context()
    {
        var requestId = Guid.NewGuid();
        var workOrderId = Guid.NewGuid();
        var source = new PostgresMaintenanceQueueReader.PageRow(
            requestId,
            "MR-1",
            "Repair sink",
            new DateOnly(2026, 8, 20),
            2,
            Guid.NewGuid(),
            "Building",
            Guid.NewGuid(),
            "Unit 1",
            Guid.NewGuid(),
            "Plumbing",
            "High",
            Guid.NewGuid(),
            "Tenant",
            workOrderId,
            "WO-1",
            Guid.NewGuid(),
            "Technician",
            new DateOnly(2026, 8, 25),
            "Unknown");

        Action action = () => PostgresMaintenanceQueueReader.MapRow(source);

        var exception = action.Should().Throw<NgbInvariantViolationException>().Which;
        exception.Context.Should().Contain("queueState", "Unknown")
            .And.Contain("requestId", requestId)
            .And.Contain("workOrderId", workOrderId);
    }
}
