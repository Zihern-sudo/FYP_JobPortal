// File: Areas/Shared/Extensions/ControllerSessionExtensions.cs
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace JobPortal.Areas.Shared.Extensions
{
    public static class ControllerSessionExtensions
    {
        /// <summary>
        /// Reads "UserId" from session like DashboardController.
        /// Returns false and sets earlyResult to a redirect (to login) or Unauthorized when invalid.
        /// </summary>
        public static bool TryGetUserId(this Controller controller, out int userId, out IActionResult? earlyResult)
        {
            userId = 0;
            earlyResult = null;

            var userIdStr = controller.HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
            {
                // Not logged in — send to Recruiter login
                earlyResult = controller.RedirectToAction("Login", "Account", new { area = "Recruiter" });
                return false;
            }

            if (!int.TryParse(userIdStr, out userId))
            {
                // Session value isn’t a valid int — treat as unauthorised
                earlyResult = controller.Unauthorized();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Flexible reader: supports int or string session values and both "UserId" and "user_id".
        /// No redirect side-effects; just returns true/false.
        /// </summary>
        public static bool TryGetUserIdFlexible(this Controller controller, out int userId)
        {
            userId = 0;
            var s = controller.HttpContext.Session;

            // 1) Try int keys
            int? byInt = s.GetInt32("UserId") ?? s.GetInt32("user_id");
            if (byInt.HasValue)
            {
                userId = byInt.Value;
                return userId > 0;
            }

            // 2) Try string keys
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
