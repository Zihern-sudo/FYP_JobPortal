// File: Areas/Shared/Extensions/ControllerSessionExtensions.cs
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Shared.Extensions
{
    public static class ControllerSessionExtensions
    {
        /// <summary>
        /// Reads UserId from session; redirects to Recruiter login when missing/invalid.
        /// </summary>
        public static bool TryGetUserId(this Controller controller, out int userId, out IActionResult? earlyResult)
        {
            userId = 0;
            earlyResult = null;

            var s = controller.HttpContext.Session;

            // Prefer int keys, then string keys
            int? byInt = s.GetInt32("UserId") ?? s.GetInt32("user_id");
            if (byInt.HasValue && byInt.Value > 0) { userId = byInt.Value; return true; }

            var byStr = s.GetString("UserId") ?? s.GetString("user_id");
            if (!string.IsNullOrWhiteSpace(byStr) && int.TryParse(byStr, out var parsed) && parsed > 0)
            { userId = parsed; return true; }

            // Not logged in — send to Recruiter login (legacy default)
            earlyResult = controller.RedirectToAction("Login", "Account", new { area = "Recruiter" });
            return false;
        }

        // ✅ area-aware overload (keep as you wrote)
        public static bool TryGetUserId(this Controller controller, string loginArea, out int userId, out IActionResult? earlyResult)
        {
            userId = 0;
            earlyResult = null;

            var s = controller.HttpContext.Session;
            int? byInt = s.GetInt32("UserId") ?? s.GetInt32("user_id");
            if (byInt.HasValue && byInt.Value > 0) { userId = byInt.Value; return true; }

            var byStr = s.GetString("UserId") ?? s.GetString("user_id");
            if (!string.IsNullOrWhiteSpace(byStr) && int.TryParse(byStr, out var parsed) && parsed > 0)
            { userId = parsed; return true; }

            earlyResult = controller.RedirectToAction("Login", "Account", new { area = loginArea });
            return false;
        }

        /// <summary>
        /// Flexible reader: supports int or string session values and both "UserId" and "user_id".
        /// No redirect side-effects; just returns true/false.
        /// </summary>
        public static bool TryGetUserIdFlexible(this Controller controller, out int userId)
        {
            userId = 0;
            var s = controller.HttpContext.Session;

            int? byInt = s.GetInt32("UserId") ?? s.GetInt32("user_id");
            if (byInt.HasValue)
            {
                userId = byInt.Value;
                return userId > 0;
            }

            var byStr = s.GetString("UserId") ?? s.GetString("user_id");
            if (!string.IsNullOrWhiteSpace(byStr) && int.TryParse(byStr, out var parsed) && parsed > 0)
            {
                userId = parsed;
                return true;
            }

            return false;
        }
    }
}
