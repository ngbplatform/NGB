using Microsoft.AspNetCore.Mvc.ApplicationModels;
using NGB.Api.Controllers;

namespace NGB.CRM.Api.Services;

public sealed class CrmApplicationSurfaceConvention : IApplicationModelConvention
{
    private static readonly HashSet<Type> DisabledControllerTypes =
    [
        typeof(AccountingPeriodClosingController),
        typeof(GeneralJournalEntriesController)
    ];

    public void Apply(ApplicationModel application)
    {
        for (var i = application.Controllers.Count - 1; i >= 0; i--)
        {
            var controller = application.Controllers[i];
            if (DisabledControllerTypes.Contains(controller.ControllerType.AsType()))
            {
                application.Controllers.RemoveAt(i);
                continue;
            }

            RemoveChartOfAccountsActions(controller);
        }
    }

    private static void RemoveChartOfAccountsActions(ControllerModel controller)
    {
        for (var i = controller.Actions.Count - 1; i >= 0; i--)
        {
            var action = controller.Actions[i];
            if (action.Selectors.Any(static selector => IsChartOfAccountsRoute(selector.AttributeRouteModel?.Template)))
                controller.Actions.RemoveAt(i);
        }
    }

    private static bool IsChartOfAccountsRoute(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
            return false;

        var normalized = template.Trim().TrimStart('~', '/');
        return normalized.StartsWith("api/chart-of-accounts", StringComparison.OrdinalIgnoreCase);
    }
}
