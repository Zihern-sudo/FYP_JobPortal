using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using JobPortal.Services;

namespace JobPortal.Areas.Shared.Models.Extensions
{
    /// <summary>
    /// Non-blocking notification helpers for MVC controllers.
    /// </summary>
    public static class ControllerNotificationExtensions
    {
        /// <remarks>Why: notifications are non-critical; never break the main action.</remarks>
        public static async Task TryNotifyAsync(this Controller controller,
                                                INotificationService notif,
                                                ILogger logger,
                                                Func<Task<int>> send)
        {
            try { _ = await send(); }
            catch (Exception ex) { logger.LogWarning(ex, "Notification send failed"); }
        }

        /// <remarks>Why: area-aware links inside notification text.</remarks>
        public static string? AreaUrl(this Controller c,
                                      string action,
                                      string controllerName,
                                      string area,
                                      object? routeValues = null)
        {
            return c.Url.Action(action, controllerName, new { area, routeValues });
        }
    }
}
