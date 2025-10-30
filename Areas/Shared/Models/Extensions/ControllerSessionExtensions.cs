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
    }
}
