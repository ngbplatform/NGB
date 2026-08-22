using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using NGB.Api.Controllers;
using NGB.CRM.Api.Services;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Services;

public sealed class CrmApplicationSurfaceConventionFullCoverageTests
{
    [Fact]
    public void Apply_RemovesDisabledControllersWhileIteratingBackwards()
    {
        var application = new ApplicationModel();
        application.Controllers.Add(Controller(typeof(AccountingPeriodClosingController)));
        application.Controllers.Add(Controller(typeof(AllowedController)));
        application.Controllers.Add(Controller(typeof(GeneralJournalEntriesController)));

        new CrmApplicationSurfaceConvention().Apply(application);

        application.Controllers.Should().ContainSingle().Which.ControllerType.AsType().Should().Be(typeof(AllowedController));
    }

    [Fact]
    public void Apply_RemovesOnlyChartOfAccountsActionsAcrossAllRouteShapes()
    {
        var controller = Controller(typeof(AllowedController));
        controller.Actions.Add(Action(nameof(AllowedController.NoSelectors)));
        controller.Actions.Add(Action(nameof(AllowedController.NullRoute), null));
        controller.Actions.Add(Action(nameof(AllowedController.WhitespaceRoute), "   "));
        controller.Actions.Add(Action(nameof(AllowedController.OtherRoute), "api/reports"));
        controller.Actions.Add(Action(nameof(AllowedController.ChartRoute), "~/API/CHART-OF-ACCOUNTS/items"));

        var multiSelector = Action(nameof(AllowedController.MultiSelectorRoute), "api/reports");
        multiSelector.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = new AttributeRouteModel(new RouteAttribute("/api/chart-of-accounts")),
        });
        controller.Actions.Add(multiSelector);

        var application = new ApplicationModel();
        application.Controllers.Add(controller);

        new CrmApplicationSurfaceConvention().Apply(application);

        controller.Actions.Select(x => x.ActionMethod.Name).Should().BeEquivalentTo(
            nameof(AllowedController.NoSelectors),
            nameof(AllowedController.NullRoute),
            nameof(AllowedController.WhitespaceRoute),
            nameof(AllowedController.OtherRoute));
    }

    private static ControllerModel Controller(Type type) => new(type.GetTypeInfo(), []);

    private static ActionModel Action(string methodName, string? route = null)
    {
        var action = new ActionModel(typeof(AllowedController).GetMethod(methodName)!, []);
        if (route is not null)
        {
            action.Selectors.Add(new SelectorModel
            {
                AttributeRouteModel = new AttributeRouteModel(new RouteAttribute(route)),
            });
        }
        else if (methodName == nameof(AllowedController.NullRoute))
        {
            action.Selectors.Add(new SelectorModel());
        }

        return action;
    }

    private sealed class AllowedController
    {
        public void NoSelectors() { }
        public void NullRoute() { }
        public void WhitespaceRoute() { }
        public void OtherRoute() { }
        public void ChartRoute() { }
        public void MultiSelectorRoute() { }
    }
}
