using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal;
using JobPortal.Areas.JobSeeker.ViewComponents;
using JobPortal.Areas.Shared.Models;

namespace JobPortal.Areas.JobSeeker.ViewComponents
{
    public class UserSidebarViewComponent : ViewComponent
    {
        private readonly AppDbContext _db;

        public UserSidebarViewComponent(AppDbContext db)
        {
            _db = db;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var userIdStr = HttpContext.Session.GetString("UserId");
            if (string.IsNullOrEmpty(userIdStr))
                return View("Default", new UserSidebarViewModel { FullName = "Guest User" });

            int userId = int.Parse(userIdStr);
            var user = await _db.users.FirstOrDefaultAsync(u => u.user_id == userId);

            if (user == null)
                return View("Default", new UserSidebarViewModel { FullName = "Guest User" });

            var vm = new UserSidebarViewModel
            {
                FullName = $"{user.first_name} {user.last_name}",
                ProfilePicturePath = string.IsNullOrEmpty(user.profile_picture)
                    ? "/images/default-avatar.png"
                    : user.profile_picture
            };

            return View("Default", vm);
        }
    }

    public class UserSidebarViewModel
    {
        public string FullName { get; set; } = "";
        public string ProfilePicturePath { get; set; } = "";
    }
}
