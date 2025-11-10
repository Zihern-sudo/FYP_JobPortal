// ======================================================================
// File: Areas/Shared/Models/Extensions/AreaRoleGuardFilter.cs
// ======================================================================
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization; // <-- new: for IAllowAnonymous
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using JobPortal.Areas.Shared.Extensions; // TryGetUserId()

namespace JobPortal.Areas.Shared.Models.Extensions
{
    /// <summary>
    /// Session-based guard for an Area requiring a specific role.
    /// Redirects to {loginArea}/Account/Login with returnUrl if unauthenticated or wrong role.
    /// Respects [AllowAnonymous] on actions/endpoints.
    /// </summary>
    public sealed class AreaRoleGuardFilter : IAsyncActionFilter
    {
        private static readonly string[] RoleKeys = { "UserRole", "role", "user_role" };

        private readonly string _requiredRole;  // e.g., "Admin" or "Recruiter"
        private readonly string _loginArea;     // e.g., "JobSeeker"

        public AreaRoleGuardFilter(string requiredRole, string loginArea)
        {
            _requiredRole = requiredRole;
            _loginArea = loginArea;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
        {
            // --- Respect [AllowAnonymous] on the endpoint (prevents bouncing public pages like Recruiter/Register)
            var endpoint = ctx.HttpContext.GetEndpoint();
            var allowAnon = endpoint?.Metadata.GetMetadata<IAllowAnonymous>() != null;
            if (allowAnon)
            {
                await next();
                return;
            }

            var req = ctx.HttpContext.Request;
            var path = req?.Path.HasValue == true ? req.Path.Value : "/";
            var query = req?.QueryString.HasValue == true ? req.QueryString.Value : string.Empty;
            var returnUrl = string.IsNullOrEmpty(path) ? "/" : (path + (query ?? string.Empty));

            if (ctx.Controller is not Controller controller)
            {
                ctx.Result = new RedirectToActionResult("Login", "Account", new { area = _loginArea, returnUrl });
                return;
            }

            // Must be logged in
            int userId;
            IActionResult? _ignored;
            if (!controller.TryGetUserId(out userId, out _ignored))
            {
                ctx.Result = new RedirectToActionResult("Login", "Account", new { area = _loginArea, returnUrl });
                return;
            }

            // Must match required role
            var role = ResolveRole(ctx.HttpContext);
            if (!string.Equals(role, _requiredRole, System.StringComparison.OrdinalIgnoreCase))
            {
                controller.TempData["Flash.Type"] = "danger";
                controller.TempData["Flash.Message"] = $"{_requiredRole} access required. Please sign in.";
                ctx.Result = new RedirectToActionResult("Login", "Account", new { area = _loginArea, returnUrl });
                return;
            }

            await next();
        }

        private static string? ResolveRole(HttpContext http)
        {
            var s = http?.Session;
            if (s == null) return null;
            foreach (var k in RoleKeys)
            {
                var v = s.GetString(k);
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            return null;
        }
    }
}
