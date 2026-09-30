using FluentAssertions;
using Moq;
using NGB.Core.Security;
using NGB.PropertyManagement.Api.Controllers;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Definitions;
using NGB.PropertyManagement.Runtime.Payables;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Controllers;

public sealed class OpenItemsControllerFullCoverageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Receivables_summary_normalizes_optional_filters_and_returns_service_result(bool supplyFilters)
    {
        var partyId = supplyFilters ? Guid.NewGuid() : (Guid?)null;
        var propertyId = supplyFilters ? Guid.NewGuid() : (Guid?)null;
        var leaseId = Guid.NewGuid();
        var expected = new ReceivablesOpenItemsResponse(Guid.NewGuid(), [], [], 12.34m, 5.67m);
        var access = GrantedAccess(
            NgbResourceKinds.Page,
            PropertyManagementSecurityDefaults.ReceivablesOpenItemsPage,
            NgbPermissionActions.View);
        var service = new Mock<IReceivablesOpenItemsService>(MockBehavior.Strict);
        service.Setup(x => x.GetOpenItemsAsync(
                partyId ?? Guid.Empty,
                propertyId ?? Guid.Empty,
                leaseId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var actual = await new ReceivablesController(access.Object).GetOpenItemsSummary(
            service.Object,
            leaseId,
            partyId,
            propertyId,
            CancellationToken.None);

        actual.Should().BeSameAs(expected);
        access.VerifyAll();
        service.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Receivables_details_normalizes_optional_filters_and_returns_service_result(bool supplyFilters)
    {
        var partyId = supplyFilters ? Guid.NewGuid() : (Guid?)null;
        var propertyId = supplyFilters ? Guid.NewGuid() : (Guid?)null;
        var leaseId = Guid.NewGuid();
        var from = supplyFilters ? DateOnly.MinValue : (DateOnly?)null;
        var to = supplyFilters ? DateOnly.MaxValue : (DateOnly?)null;
        var expected = new ReceivablesOpenItemsDetailsResponse(
            Guid.NewGuid(),
            partyId ?? Guid.Empty,
            null,
            propertyId ?? Guid.Empty,
            null,
            leaseId,
            null,
            [],
            [],
            [],
            0m,
            0m);
        var access = GrantedAccess(
            NgbResourceKinds.Page,
            PropertyManagementSecurityDefaults.ReceivablesOpenItemsPage,
            NgbPermissionActions.View);
        var service = new Mock<IReceivablesOpenItemsDetailsService>(MockBehavior.Strict);
        service.Setup(x => x.GetOpenItemsDetailsAsync(
                partyId ?? Guid.Empty,
                propertyId ?? Guid.Empty,
                leaseId,
                from,
                to,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var actual = await new ReceivablesController(access.Object).GetOpenItems(
            service.Object,
            leaseId,
            partyId,
            propertyId,
            from,
            to,
            CancellationToken.None);

        actual.Should().BeSameAs(expected);
        access.VerifyAll();
        service.VerifyAll();
    }

    [Fact]
    public async Task Payables_open_items_forwards_boundary_dates_and_returns_service_result()
    {
        var partyId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var from = DateOnly.MinValue;
        var to = DateOnly.MaxValue;
        var expected = new PayablesOpenItemsDetailsResponse(
            Guid.NewGuid(), partyId, null, propertyId, null, [], [], [], 0m, 0m);
        var access = GrantedAccess(
            NgbResourceKinds.Page,
            PropertyManagementSecurityDefaults.PayablesOpenItemsPage,
            NgbPermissionActions.View);
        var service = new Mock<IPayablesOpenItemsDetailsService>(MockBehavior.Strict);
        service.Setup(x => x.GetOpenItemsDetailsAsync(
                partyId,
                propertyId,
                from,
                to,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var actual = await new PayablesController(access.Object).GetOpenItems(
            service.Object,
            partyId,
            propertyId,
            from,
            to,
            CancellationToken.None);

        actual.Should().BeSameAs(expected);
        access.VerifyAll();
        service.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Receivables_paged_details_forwards_explicit_values_and_applies_safe_defaults(bool explicitPaging)
    {
        var leaseId = Guid.NewGuid();
        var partyId = explicitPaging ? Guid.NewGuid() : (Guid?)null;
        var propertyId = explicitPaging ? Guid.NewGuid() : (Guid?)null;
        var expected = new ReceivablesOpenItemsDetailsResponse(
            Guid.NewGuid(), partyId ?? Guid.Empty, null, propertyId ?? Guid.Empty, null, leaseId, null, [], [], [], 0m, 0m);
        var delayedResult = explicitPaging
            ? new TaskCompletionSource<ReceivablesOpenItemsDetailsResponse>(
                TaskCreationOptions.RunContinuationsAsynchronously)
            : null;
        var access = GrantedAccess(NgbResourceKinds.Page, PropertyManagementSecurityDefaults.ReceivablesOpenItemsPage, NgbPermissionActions.View);
        var service = new Mock<IReceivablesOpenItemsDetailsService>(MockBehavior.Strict);
        service.Setup(x => x.GetOpenItemsDetailsPageAsync(
                partyId ?? Guid.Empty, propertyId ?? Guid.Empty, leaseId, DateOnly.MinValue, DateOnly.MaxValue,
                explicitPaging ? 10 : 0,
                explicitPaging ? 20 : 0,
                explicitPaging ? 30 : 0,
                explicitPaging ? 40 : 100,
                It.IsAny<CancellationToken>()))
            .Returns(() => delayedResult?.Task ?? Task.FromResult(expected));

        var operation = new ReceivablesController(access.Object).GetOpenItemsDetailsPage(
            service.Object, leaseId, partyId, propertyId, DateOnly.MinValue, DateOnly.MaxValue,
            explicitPaging ? 10 : null,
            explicitPaging ? 20 : null,
            explicitPaging ? 30 : null,
            explicitPaging ? 40 : null,
            CancellationToken.None);
        if (delayedResult is not null)
        {
            operation.IsCompleted.Should().BeFalse();
            delayedResult.SetResult(expected);
        }

        var actual = await operation;

        actual.Should().BeSameAs(expected);
        access.VerifyAll();
        service.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Payables_paged_details_forwards_explicit_values_and_applies_safe_defaults(bool explicitPaging)
    {
        var partyId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var expected = new PayablesOpenItemsDetailsResponse(
            Guid.NewGuid(), partyId, null, propertyId, null, [], [], [], 0m, 0m);
        var access = GrantedAccess(NgbResourceKinds.Page, PropertyManagementSecurityDefaults.PayablesOpenItemsPage, NgbPermissionActions.View);
        var service = new Mock<IPayablesOpenItemsDetailsService>(MockBehavior.Strict);
        service.Setup(x => x.GetOpenItemsDetailsPageAsync(
                partyId, propertyId, DateOnly.MinValue, DateOnly.MaxValue,
                explicitPaging ? 10 : 0,
                explicitPaging ? 20 : 0,
                explicitPaging ? 30 : 0,
                explicitPaging ? 40 : 100,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var actual = await new PayablesController(access.Object).GetOpenItemsDetailsPage(
            service.Object, partyId, propertyId, DateOnly.MinValue, DateOnly.MaxValue,
            explicitPaging ? 10 : null,
            explicitPaging ? 20 : null,
            explicitPaging ? 30 : null,
            explicitPaging ? 40 : null,
            CancellationToken.None);

        actual.Should().BeSameAs(expected);
        access.VerifyAll();
        service.VerifyAll();
    }

    [Fact]
    public async Task Access_failure_stops_receivables_summary_before_service_execution()
    {
        var denied = new UnauthorizedAccessException("denied");
        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        access.Setup(x => x.RequireAsync(
                NgbResourceKinds.Page,
                PropertyManagementSecurityDefaults.ReceivablesOpenItemsPage,
                NgbPermissionActions.View,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(denied);
        var service = new Mock<IReceivablesOpenItemsService>(MockBehavior.Strict);

        Func<Task> act = () => new ReceivablesController(access.Object).GetOpenItemsSummary(
            service.Object,
            Guid.Empty,
            null,
            null,
            CancellationToken.None);

        (await act.Should().ThrowAsync<UnauthorizedAccessException>()).Which.Should().BeSameAs(denied);
        access.VerifyAll();
        service.VerifyNoOtherCalls();
    }

    private static Mock<INgbAccessChecker> GrantedAccess(string resourceKind, string resourceCode, string action)
    {
        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        access.Setup(x => x.RequireAsync(resourceKind, resourceCode, action, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return access;
    }
}
