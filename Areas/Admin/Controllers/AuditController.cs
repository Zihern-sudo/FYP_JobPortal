// ================================
// File: Areas/Admin/Controllers/AuditController.cs
// ================================
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.ViewEngines;   // ICompositeViewEngine
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Text;
using SelectPdf;

using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Admin.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class AuditController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ICompositeViewEngine _viewEngine; // ✅ use composite (always registered)
        private readonly ITempDataProvider _tempDataProvider;

        public AuditController(
            AppDbContext db,
            ICompositeViewEngine viewEngine,
            ITempDataProvider tempDataProvider)
        {
            _db = db;
            _viewEngine = viewEngine;
            _tempDataProvider = tempDataProvider;
        }

        // GET: /Admin/Audit
        public IActionResult Index(
            DateTime? from = null,
            DateTime? to = null,
            string? actor = null,
            string? actionType = null,   // ✅ renamed to avoid route/action collision
            string? target = null,
            int page = 1,
            int pageSize = 20)
        {
            ViewData["Title"] = "Audit Log";

            var query = BuildQuery(from, to, actor, actionType, target);

            var filteredAll = query
                .Select(x => new
                {
                    x.timestamp,
                    Actor = (x.user != null ? (x.user.first_name + " " + x.user.last_name).Trim() : "System"),
                    Action = x.action_type
                });

            var topActions = filteredAll
                .GroupBy(x => x.Action ?? "Unknown")
                .Select(g => new { Key = g.Key, Cnt = g.Count() })
                .OrderByDescending(x => x.Cnt)
                .Take(5)
                .ToList();

            var topActors = filteredAll
                .GroupBy(x => x.Actor ?? "System")
                .Select(g => new { Key = g.Key, Cnt = g.Count() })
                .OrderByDescending(x => x.Cnt)
                .Take(5)
                .ToList();

            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 5, 200);

            // ✅ total BEFORE paging
            var total = query.Count();

            var items = query
                .OrderByDescending(x => x.timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new AuditLogViewModel
                {
                    When = x.timestamp,
                    Actor = (x.user != null ? (x.user.first_name + " " + x.user.last_name).Trim() : "System"),
                    Action = x.action_type,
                    Target = null,
                    Notes = null
                })
                .ToList();

            ViewBag.DateFrom = from;
            ViewBag.DateTo = to;
            ViewBag.Actor = actor ?? string.Empty;
            ViewBag.Action = actionType ?? string.Empty;  // ✅ keep available to the view
            ViewBag.Target = target ?? string.Empty;

            ViewBag.TopActions = topActions;
            ViewBag.TopActors = topActors;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;

            return View(items);
        }

        // GET: /Admin/Audit/ExportCsv
        [HttpGet]
        public IActionResult ExportCsv(
            DateTime? from = null,
            DateTime? to = null,
            string? actor = null,
            string? actionType = null,   // ✅
            string? target = null)
        {
            var query = BuildQuery(from, to, actor, actionType, target);

            var rows = query
                .OrderByDescending(x => x.timestamp)
                .Select(x => new
                {
                    When = x.timestamp,
                    Actor = (x.user != null ? (x.user.first_name + " " + x.user.last_name).Trim() : "System"),
                    Action = x.action_type,
                    Target = (string?)null,
                    Notes = (string?)null
                })
                .ToList();

            static string Esc(string? s)
            {
                if (string.IsNullOrEmpty(s)) return "";
                var needs = s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r');
                var t = s.Replace("\"", "\"\"");
                return needs ? $"\"{t}\"" : t;
            }

            var sb = new StringBuilder();
            sb.AppendLine("When,Actor,Action,Target,Notes");
            foreach (var r in rows)
            {
                sb.AppendLine($"{r.When:yyyy-MM-dd HH:mm},{Esc(r.Actor)},{Esc(r.Action)},{Esc(r.Target)},{Esc(r.Notes)}");
            }

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"Audit_{DateTime.UtcNow:yyyyMMdd_HHmm}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        // GET: /Admin/Audit/ExportPdf
        [HttpGet]
        public async Task<IActionResult> ExportPdf(
            DateTime? from = null,
            DateTime? to = null,
            string? actor = null,
            string? actionType = null,   // ✅
            string? target = null)
        {
            var query = BuildQuery(from, to, actor, actionType, target);

            var items = query
                .OrderByDescending(x => x.timestamp)
                .Select(x => new AuditLogViewModel
                {
                    When = x.timestamp,
                    Actor = (x.user != null ? (x.user.first_name + " " + x.user.last_name).Trim() : "System"),
                    Action = x.action_type,
                    Target = null,
                    Notes = null
                })
                .ToList();

            ViewBag.DateFrom = from;
            ViewBag.DateTo = to;
            ViewBag.Actor = actor ?? string.Empty;
            ViewBag.Action = actionType ?? string.Empty; // ✅
            ViewBag.Target = target ?? string.Empty;
            ViewBag.GeneratedAt = DateTime.UtcNow;

            var html = await RenderRazorViewToString("Pdf", items);

            var converter = new HtmlToPdf
            {
                Options =
                {
                    PdfPageSize = PdfPageSize.A4,
                    PdfPageOrientation = PdfPageOrientation.Portrait,
                    MarginTop = 20, MarginRight = 20, MarginBottom = 20, MarginLeft = 20,
                    WebPageWidth = 1024
                }
            };

            converter.Options.DisplayHeader = true;
            converter.Header.Height = 48;
            converter.Header.Add(new PdfHtmlSection(
                "<div style='font-family:Arial; font-size:11px; width:100%'>" +
                "<div style='float:left'>JobPortal — Audit Report</div>" +
                $"<div style='float:right'>{DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC</div>" +
                "<div style='clear:both'></div></div>", string.Empty));

            converter.Options.DisplayFooter = true;
            converter.Footer.Height = 40;
            converter.Footer.Add(new PdfHtmlSection(
                "<div style='font-family:Arial; font-size:9px; width:100%'>" +
                $"<div style='float:left'>&copy; {DateTime.UtcNow:yyyy} JobPortal</div>" +
                "<div style='float:right'>Page {page_number} of {total_pages}</div>" +
                "<div style='clear:both'></div></div>", string.Empty));

            var pdf = converter.ConvertHtmlString(html);
            var bytes = pdf.Save();
            pdf.Close();

            return File(bytes, "application/pdf", $"Audit_{DateTime.UtcNow:yyyyMMdd}.pdf");
        }

        // --- helpers ---
        private IQueryable<admin_log> BuildQuery(DateTime? from, DateTime? to, string? actor, string? actionType, string? target)
        {
            var q = _db.admin_logs
                .AsNoTracking()
                .Include(l => l.user)
                .AsQueryable();

            if (from.HasValue)
            {
                var f = from.Value.Date;
                q = q.Where(x => x.timestamp >= f);
            }
            if (to.HasValue)
            {
                var t = to.Value.Date.AddDays(1);
                q = q.Where(x => x.timestamp < t);
            }

            if (!string.IsNullOrWhiteSpace(actor))
            {
                var term = actor.Trim();
                q = q.Where(x =>
                    ((x.user.first_name + " " + x.user.last_name).Contains(term)) ||
                    x.user.email.Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(actionType))
            {
                var term = actionType.Trim();
                q = q.Where(x => EF.Functions.Like(x.action_type, $"%{term}%"));
            }

            // Target: placeholder (schema has no dedicated columns)
            if (!string.IsNullOrWhiteSpace(target))
            {
                // no-op for now
            }

            return q;
        }

        private async Task<string> RenderRazorViewToString(string viewName, object model)
        {
            using var sw = new System.IO.StringWriter();

            var viewResult = _viewEngine.FindView(ControllerContext, viewName, isMainPage: true);
            if (!viewResult.Success)
            {
                var absolute = $"/Areas/Admin/Views/Audit/{viewName}.cshtml";
                viewResult = _viewEngine.GetView(executingFilePath: null, viewPath: absolute, isMainPage: true);
            }
            if (!viewResult.Success || viewResult.View == null)
                throw new ArgumentNullException($"Could not find view: {viewName}");

            var vdd = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = model
            };

            var tempData = new TempDataDictionary(HttpContext, _tempDataProvider);
            var vc = new ViewContext(ControllerContext, viewResult.View, vdd, tempData, sw, new HtmlHelperOptions());
            await viewResult.View.RenderAsync(vc);
            return sw.ToString();
        }
    }
}
