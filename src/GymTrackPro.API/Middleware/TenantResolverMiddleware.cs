using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using GymTrackPro.API.Data;
using GymTrackPro.Shared.Entities;
using GymTrackPro.Shared.Enums;

namespace GymTrackPro.API.Middleware;

public class TenantResolverMiddleware
{
    private readonly RequestDelegate _next;

    public TenantResolverMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, GymDbContext dbContext, TenantState tenantState)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var emailClaim = context.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email || c.Type == "email")?.Value;
            var uidClaim = context.User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier || c.Type == "user_id")?.Value;

            if (!string.IsNullOrEmpty(emailClaim))
            {
                // Bypass query filters to lookup the user's tenant assignment
                var user = await dbContext.Users
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(u => u.Email == emailClaim);

                if (user != null)
                {
                    tenantState.GymID = user.GymID;
                    tenantState.UserRole = user.Role.ToString();

                    // Store in HttpContext items for access in other middleware / diagnostic logging
                    context.Items["GymID"] = user.GymID;
                    context.Items["UserRole"] = user.Role.ToString();

                    // Add identity containing local database role to ClaimsPrincipal so [Authorize(Roles = ...)] works.
                    var claims = new System.Collections.Generic.List<Claim>
                    {
                        new Claim(ClaimTypes.Role, user.Role.ToString())
                    };
                    if (user.Role == UserRole.PlatformAdmin || user.Role == UserRole.GymOwner)
                    {
                        claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
                    }
                    var identity = new ClaimsIdentity(claims);
                    context.User.AddIdentity(identity);
                }
            }
        }

        await _next(context);
    }
}
