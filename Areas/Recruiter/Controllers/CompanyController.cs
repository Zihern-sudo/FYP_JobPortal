using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Shared.Extensions;
using JobPortal.Areas.Recruiter.Models;

namespace JobPortal.Areas.Recruiter.Controllers
{
    [Area("Recruiter")]
    public class CompanyController : Controller
    {
        private readonly AppDbContext _db;
        public CompanyController(AppDbContext db) => _db = db;

        [HttpGet]
        public async Task<IActionResult> Manage()
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;

            var comp = await _db.companies.FirstOrDefaultAsync(c => c.user_id == recruiterId);
            var vm = comp == null
                ? new CompanyProfileVm()
                : new CompanyProfileVm
                {
                    company_name = comp.company_name,
                    company_industry = comp.company_industry,
                    company_location = comp.company_location,
                    company_description = comp.company_description
                };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Manage(CompanyProfileVm vm)
        {
            if (!this.TryGetUserId(out var recruiterId, out var early)) return early!;
            if (!ModelState.IsValid) return View(vm);

            var comp = await _db.companies.FirstOrDefaultAsync(c => c.user_id == recruiterId);
            if (comp == null)
            {
                comp = new company
                {
                    user_id = recruiterId,
                    company_name = vm.company_name.Trim(),
                    company_industry = vm.company_industry?.Trim(),
                    company_location = vm.company_location?.Trim(),
                    company_description = vm.company_description,
                    company_status = "Pending" // why: admin approval flow
                };
                _db.companies.Add(comp);
            }
            else
            {
                comp.company_name = vm.company_name.Trim();
                comp.company_industry = vm.company_industry?.Trim();
                comp.company_location = vm.company_location?.Trim();
                comp.company_description = vm.company_description;
                // keep status as-is; admin controls lifecycle
            }

            await _db.SaveChangesAsync();
            TempData["Message"] = "Company profile saved.";
            return RedirectToAction("Index", "Home", new { area = "Recruiter" });
        }
    }
}
