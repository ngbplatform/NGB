using Hangfire.Dashboard;

namespace NGB.BackgroundJobs.Infrastructure;

/// <summary>
/// This is just STUB Hangfire Dashboard Authorization.
/// <remarks>
/// See extension method <c>GlobalCookieRequireAuthorization()</c>  
/// </remarks>
/// </summary>
public class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext dashboardContext)
    {
        var context = dashboardContext.GetHttpContext();
        return context.User.Identity?.IsAuthenticated ?? false;
    }
}