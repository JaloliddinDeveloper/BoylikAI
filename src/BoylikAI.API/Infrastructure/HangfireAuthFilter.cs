using Hangfire.Dashboard;

namespace BoylikAI.API.Infrastructure;

/// <summary>
/// Production Hangfire dashboard auth filter.
/// Requires the user to be authenticated and have the "Admin" role claim.
/// Replace with your actual identity/role check as needed.
/// </summary>
public sealed class HangfireAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.User.IsInRole("Admin");
    }
}
