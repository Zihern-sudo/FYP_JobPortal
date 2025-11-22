using Microsoft.AspNetCore.Mvc;
using JobPortal.Areas.Shared.Models;
using JobPortal.Areas.Admin.Models;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using SelectPdf;
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Threading.Tasks;
using System.IO;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Globalization;
using Microsoft.AspNetCore.Hosting; // inject env for logo

namespace JobPortal.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ReportsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ICompositeViewEngine _viewEngine;
        private readonly IWebHostEnvironment _env;

        // Simple DTOs to pass insight data to Razor via ViewBag
        public class KeyIndicator
        {
            public string Label { get; set; } = "";
            public string Value { get; set; } = "";
            public string? Note { get; set; }
        }

        public class InsightItem
        {
            public string Title { get; set; } = "";
            public string Detail { get; set; } = "";
            public string Level { get; set; } = "info"; // info | warn | critical
        }

        public class ActionItem
        {
            public string Title { get; set; } = "";
            public string Why { get; set; } = "";
        }

        public ReportsController(AppDbContext db, ICompositeViewEngine viewEngine, IWebHostEnvironment env)
        {
            _db = db;
            _viewEngine = viewEngine;
            _env = env;
        }

        public IActionResult Index(ReportFilterViewModel filters)
        {
            ViewData["Title"] = "Reports";

            var cards = GetReportData(filters);

            var vm = new ReportViewModel
            {
                Filters = filters,
                StatCards = cards,
                CompanyList = new SelectList(_db.companies.OrderBy(c => c.company_name), "company_id", "company_name", filters.CompanyId)
            };

            return View(vm);
        }

        // Cards (honor filters)
        private List<ReportStatCardViewModel> GetReportData(ReportFilterViewModel filters)
        {
            IQueryable<user> userQuery = _db.users.AsQueryable();
            IQueryable<company> coQuery = _db.companies.Include(c => c.user);
            IQueryable<job_listing> jobQuery = _db.job_listings.AsQueryable();
            IQueryable<job_application> appQuery = _db.job_applications.Include(a => a.job_listing);

            if (filters.DateFrom.HasValue)
            {
                var from = filters.DateFrom.Value.Date;
                userQuery = userQuery.Where(u => u.created_at >= from);
                coQuery = coQuery.Where(c => c.user.created_at >= from);
                jobQuery = jobQuery.Where(j => j.date_posted >= from);
                appQuery = appQuery.Where(a => a.date_updated >= from);
            }

            if (filters.DateTo.HasValue)
            {
                var toExclusive = filters.DateTo.Value.Date.AddDays(1);
                userQuery = userQuery.Where(u => u.created_at < toExclusive);
                coQuery = coQuery.Where(c => c.user.created_at < toExclusive);
                jobQuery = jobQuery.Where(j => j.date_posted < toExclusive);
                appQuery = appQuery.Where(a => a.date_updated < toExclusive);
            }

            if (filters.CompanyId.HasValue)
            {
                var cid = filters.CompanyId.Value;
                coQuery = coQuery.Where(c => c.company_id == cid);
                jobQuery = jobQuery.Where(j => j.company_id == cid);
                appQuery = appQuery.Where(a => a.job_listing.company_id == cid);
            }

            return new()
            {
                new() { Label = "Total Users",        Value = userQuery.Count().ToString("N0") },
                new() { Label = "Total Companies",    Value = coQuery.Count().ToString("N0") },
                new() { Label = "Total Job Listings", Value = jobQuery.Count().ToString("N0") },
                new() { Label = "Total Applications", Value = appQuery.Count().ToString("N0") }
            };
        }

        // Daily rows (honor filters)
        private IReadOnlyList<DailyReportRow> GetDailyData(ReportFilterViewModel filters)
        {
            var to = (filters.DateTo?.Date ?? DateTime.UtcNow.Date);
            var from = (filters.DateFrom?.Date ?? to.AddDays(-29));
            if (from > to) (from, to) = (to, from);

            var jobs = _db.job_listings.AsQueryable();
            var apps = _db.job_applications.Include(a => a.job_listing).AsQueryable();

            if (filters.CompanyId.HasValue)
            {
                var cid = filters.CompanyId.Value;
                jobs = jobs.Where(j => j.company_id == cid);
                apps = apps.Where(a => a.job_listing.company_id == cid);
            }

            jobs = jobs.Where(j => j.date_posted >= from && j.date_posted < to.AddDays(1));
            apps = apps.Where(a => a.date_updated >= from && a.date_updated < to.AddDays(1));

            var jobCounts = jobs
                .GroupBy(j => j.date_posted.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToDictionary(x => x.Day, x => x.Count);

            var appCounts = apps
                .GroupBy(a => a.date_updated.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToDictionary(x => x.Day, x => x.Count);

            var rows = new List<DailyReportRow>();
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                rows.Add(new DailyReportRow
                {
                    Date = d,
                    Jobs = jobCounts.TryGetValue(d, out var jc) ? jc : 0,
                    Applications = appCounts.TryGetValue(d, out var ac) ? ac : 0
                });
            }
            return rows;
        }

        // Top companies by applications (honor filters)
        private IReadOnlyList<TopCompanyRow> GetTopCompanies(ReportFilterViewModel filters, int top = 10)
        {
            var apps = _db.job_applications
                .Include(a => a.job_listing)
                .ThenInclude(j => j.company)
                .AsQueryable();

            if (filters.DateFrom.HasValue)
                apps = apps.Where(a => a.date_updated >= filters.DateFrom.Value.Date);
            if (filters.DateTo.HasValue)
                apps = apps.Where(a => a.date_updated < filters.DateTo.Value.Date.AddDays(1));
            if (filters.CompanyId.HasValue)
                apps = apps.Where(a => a.job_listing.company_id == filters.CompanyId.Value);

            var baseJobs = _db.job_listings.AsQueryable();
            if (filters.CompanyId.HasValue)
                baseJobs = baseJobs.Where(j => j.company_id == filters.CompanyId.Value);
            if (filters.DateFrom.HasValue)
                baseJobs = baseJobs.Where(j => j.date_posted >= filters.DateFrom.Value.Date);
            if (filters.DateTo.HasValue)
                baseJobs = baseJobs.Where(j => j.date_posted < filters.DateTo.Value.Date.AddDays(1));

            var jobCounts = baseJobs
                .GroupBy(j => j.company_id)
                .Select(g => new { CompanyId = g.Key, Jobs = g.Count() });

            var data = apps
                .GroupBy(a => new { a.job_listing.company_id, a.job_listing.company.company_name })
                .Select(g => new { g.Key.company_id, g.Key.company_name, Applications = g.Count() })
                .Join(jobCounts,
                      a => a.company_id,
                      j => j.CompanyId,
                      (a, j) => new TopCompanyRow
                      {
                          CompanyId = a.company_id,
                          CompanyName = a.company_name,
                          Applications = a.Applications,
                          JobListings = j.Jobs
                      })
                .OrderByDescending(x => x.Applications)
                .ThenBy(x => x.CompanyName)
                .Take(top)
                .ToList();

            return data;
        }

        // CSV (filtered) + Top companies section (unchanged)
        public IActionResult ExportCsv(ReportFilterViewModel filters)
        {
            var cards = GetReportData(filters);
            var tops = GetTopCompanies(filters, top: 10);
            static string Q(string? s) => $"\"{(s ?? string.Empty).Replace("\"", "\"\"")}\"";

            var companyName = filters.CompanyId.HasValue
                ? (_db.companies.Find(filters.CompanyId.Value)?.company_name ?? $"ID: {filters.CompanyId}")
                : "All Companies";

            var totalApps = tops.Sum(t => t.Applications);
            var totalJobs = tops.Sum(t => t.JobListings);

            var sb = new StringBuilder();
            sb.AppendLine(Q("JobPortal Admin Report"));
            sb.AppendLine($"{Q("Generated")},{Q(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'"))}");
            sb.AppendLine();

            sb.AppendLine(Q("Filters"));
            sb.AppendLine($"{Q("Field")},{Q("Value")}");
            sb.AppendLine($"{Q("Date From")},{Q(filters.DateFrom?.ToShortDateString() ?? "N/A")}");
            sb.AppendLine($"{Q("Date To")},{Q(filters.DateTo?.ToShortDateString() ?? "N/A")}");
            sb.AppendLine($"{Q("Company")},{Q(companyName)}");
            sb.AppendLine();

            sb.AppendLine(Q("Summary Metrics"));
            sb.AppendLine($"{Q("Metric")},{Q("Value")}");
            foreach (var c in cards)
                sb.AppendLine($"{Q(c.Label)},{Q(c.Value)}");
            sb.AppendLine();

            sb.AppendLine(Q("Top Companies by Applications (Top 10)"));
            sb.AppendLine($"{Q("Rank")},{Q("Company")},{Q("Applications")},{Q("Job Listings")},{Q("% of Applications")}");

            if (tops.Any())
            {
                for (int i = 0; i < tops.Count; i++)
                {
                    var t = tops[i];
                    var share = totalApps > 0 ? (t.Applications / (double)totalApps).ToString("P1", CultureInfo.InvariantCulture) : "0.0%";
                    sb.AppendLine(
                        $"{Q((i + 1).ToString())}," +
                        $"{Q(t.CompanyName)}," +
                        $"{Q(t.Applications.ToString())}," +
                        $"{Q(t.JobListings.ToString())}," +
                        $"{Q(share)}"
                    );
                }
                sb.AppendLine($"{Q("Total")},{Q(string.Empty)},{Q(totalApps.ToString())},{Q(totalJobs.ToString())},{Q("100%")}");
            }
            else
            {
                sb.AppendLine($"{Q("-")},{Q("No data for selected filters")},{Q("0")},{Q("0")},{Q("0.0%")}");
            }

            var csv = Encoding.UTF8.GetBytes(sb.ToString());
            return File(csv, "text/csv", $"JobPortal_Report_{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        // PDF (filtered) — table-less view; embeds logo via base64; adds insights
        public async Task<IActionResult> ExportPdf(ReportFilterViewModel filters)
        {
            var cards = GetReportData(filters);
            var dailyRows = GetDailyData(filters);
            var tops = GetTopCompanies(filters);

            var companyName = filters.CompanyId.HasValue
                ? (_db.companies.Find(filters.CompanyId.Value)?.company_name ?? $"ID: {filters.CompanyId}")
                : "All Companies";

            var pdfViewModel = new PdfReportViewModel
            {
                Filters = filters,
                CompanyName = companyName,
                StatCards = cards,
                DailyRows = dailyRows,
                TopCompanies = tops,
                ReportDate = DateTime.UtcNow
            };

            // ----- Build Insights & KPIs -----
            var totalJobs = dailyRows.Sum(r => r.Jobs);
            var totalApps = dailyRows.Sum(r => r.Applications);

            // Applications per Job
            var apj = (totalJobs > 0) ? (totalApps / (double)totalJobs) : 0.0;

            // Trend: last 7d vs previous 7d (applications)
            int days = dailyRows.Count;
            int last7 = dailyRows.Skip(Math.Max(0, days - 7)).Sum(r => r.Applications);
            int prev7 = dailyRows.Skip(Math.Max(0, days - 14)).Take(Math.Max(0, Math.Min(7, days - 7))).Sum(r => r.Applications);
            double trendPct = (prev7 == 0) ? (last7 > 0 ? 100.0 : 0.0) : ((last7 - prev7) / (double)prev7) * 100.0;

            // Busiest day by applications
            var busiest = dailyRows.OrderByDescending(r => r.Applications).FirstOrDefault();
            var busiestLabel = busiest != null ? $"{busiest.Date:yyyy-MM-dd} ({busiest.Applications} apps)" : "N/A";

            // Inactivity (days with zero jobs & apps in period)
            int zeroDays = dailyRows.Count(r => r.Jobs == 0 && r.Applications == 0);

            // Concentration: top company share(s)
            var topShare = 0.0;
            var top3Share = 0.0;
            if (totalApps > 0 && tops.Any())
            {
                topShare = tops.First().Applications / (double)totalApps;
                top3Share = tops.Take(3).Sum(t => t.Applications) / (double)totalApps;
            }

            var indicators = new List<KeyIndicator>
            {
                new KeyIndicator { Label = "Applications / Job", Value = apj.ToString("0.00"), Note = "Efficiency of postings" },
                new KeyIndicator { Label = "7-day Application Trend", Value = (trendPct >= 0 ? "+" : "") + trendPct.ToString("0.#") + "%", Note = $"Last 7d ({last7}) vs prev 7d ({prev7})" },
                new KeyIndicator { Label = "Busiest Day", Value = busiestLabel, Note = "Peak application activity" },
                new KeyIndicator { Label = "Inactive Days", Value = zeroDays.ToString("N0"), Note = "No jobs or applications" },
                new KeyIndicator { Label = "Top Company Share", Value = (topShare*100).ToString("0.#") + "%", Note = tops.Any() ? tops.First().CompanyName : "N/A" },
                new KeyIndicator { Label = "Top 3 Concentration", Value = (top3Share*100).ToString("0.#") + "%", Note = "Share of all applications" },
            };

            var insights = new List<InsightItem>();

            // Insight: Low APJ
            if (apj < 1.0 && totalJobs > 0)
            {
                insights.Add(new InsightItem
                {
                    Title = "Low conversions per job",
                    Detail = $"Applications per job is {apj:0.00}. Consider improving job descriptions or reach.",
                    Level = "warn"
                });
            }

            // Insight: Strong positive/negative trend
            if (trendPct >= 25)
            {
                insights.Add(new InsightItem
                {
                    Title = "Applications trending up",
                    Detail = $"Last 7 days are up {trendPct:0.#}% vs previous week ({last7} vs {prev7}).",
                    Level = "info"
                });
            }
            else if (trendPct <= -25)
            {
                insights.Add(new InsightItem
                {
                    Title = "Applications trending down",
                    Detail = $"Last 7 days are down {trendPct:0.#}% vs previous week ({last7} vs {prev7}).",
                    Level = "warn"
                });
            }

            // Insight: Concentration risk
            if (topShare >= 0.5)
            {
                insights.Add(new InsightItem
                {
                    Title = "High company concentration",
                    Detail = $"Top company accounts for {(topShare * 100):0.#}% of applications ({(tops.FirstOrDefault()?.CompanyName ?? "N/A")}).",
                    Level = "critical"
                });
            }
            else if (top3Share >= 0.7)
            {
                insights.Add(new InsightItem
                {
                    Title = "Top-3 concentration notable",
                    Detail = $"Top 3 companies take {(top3Share * 100):0.#}% of all applications.",
                    Level = "warn"
                });
            }

            // Insight: Operational inactivity
            if (zeroDays >= Math.Max(3, dailyRows.Count / 5))
            {
                insights.Add(new InsightItem
                {
                    Title = "Frequent inactive days",
                    Detail = $"{zeroDays} days in the period with no postings or applications.",
                    Level = "info"
                });
            }

            var actions = new List<ActionItem>();
            if (apj < 1.2)
            {
                actions.Add(new ActionItem
                {
                    Title = "Review job quality",
                    Why = "Low applications per job. Improve titles, benefits, and keywords."
                });
            }
            if (trendPct <= -25)
            {
                actions.Add(new ActionItem
                {
                    Title = "Boost outreach",
                    Why = "Sharp week-over-week decline. Promote selected roles or email dormant candidates."
                });
            }
            if (topShare >= 0.5)
            {
                actions.Add(new ActionItem
                {
                    Title = "Balance employer mix",
                    Why = "Reduce dependency on a single company; onboard 1–2 new employers."
                });
            }

            // Build base64 data URL for logo so PDF engine can render it without file path issues.
            ViewBag.LogoDataUrl = BuildLogoDataUrl();
            ViewBag.KeyIndicators = indicators;
            ViewBag.Insights = insights;
            ViewBag.Recommendations = actions;

            // Ensure the view receives all dynamic data (for RenderRazorViewToString path).
            ViewData["LogoDataUrl"] = ViewBag.LogoDataUrl;
            ViewData["KeyIndicators"] = indicators;
            ViewData["Insights"] = insights;
            ViewData["Recommendations"] = actions;

            // Render Razor view -> HTML
            var html = await RenderRazorViewToString("Pdf", pdfViewModel);

            // Convert HTML -> PDF
            var converter = new HtmlToPdf
            {
                Options =
                {
                    PdfPageSize         = PdfPageSize.A4,
                    PdfPageOrientation  = PdfPageOrientation.Portrait,
                    MarginTop           = 20, MarginRight = 20, MarginBottom = 20, MarginLeft = 20,
                    WebPageWidth        = 1024
                }
            };

            // Header
            converter.Options.DisplayHeader = true;
            converter.Header.Height = 50;
            converter.Header.Add(new PdfHtmlSection(
                $"<div style='font-family:Arial; font-size:11px; width:100%'>" +
                $"  <div style='float:left'>JobPortal — Admin Report</div>" +
                $"  <div style='float:right'>{pdfViewModel.ReportDate:yyyy-MM-dd HH:mm} UTC</div>" +
                $"  <div style='clear:both'></div>" +
                $"</div>", string.Empty));

            // Footer
            converter.Options.DisplayFooter = true;
            converter.Footer.Height = 40;
            converter.Footer.Add(new PdfHtmlSection(
                $"<div style='font-family:Arial; font-size:9px'>&copy; {pdfViewModel.ReportDate:yyyy} JobPortal</div>",
                string.Empty));

            var pageText = new PdfTextSection(0, 0, "Page {page_number} of {total_pages}",
                new System.Drawing.Font("Arial", 9))
            {
                HorizontalAlign = PdfTextHorizontalAlign.Right
            };
            converter.Footer.Add(pageText);

            var pdfDoc = converter.ConvertHtmlString(html);
            var pdfBytes = pdfDoc.Save();
            pdfDoc.Close();

            return File(pdfBytes, "application/pdf", $"JobPortal_Report_{DateTime.UtcNow:yyyyMMdd}.pdf");
        }

        private string? BuildLogoDataUrl()
        {
            // Why: data URI avoids deployment/path issues in HTML-to-PDF.
            try
            {
                var logoPath = Path.Combine(_env.WebRootPath ?? string.Empty, "img", "FYP_Logo.jpg");
                if (!System.IO.File.Exists(logoPath)) return null;

                var bytes = System.IO.File.ReadAllBytes(logoPath);
                var b64 = Convert.ToBase64String(bytes);
                return $"data:image/jpeg;base64,{b64}";
            }
            catch
            {
                return null;
            }
        }

        // Razor view to string (uses current ControllerContext)
        private async Task<string> RenderRazorViewToString(string viewName, object model)
        {
            using var sw = new StringWriter();

            var viewResult = _viewEngine.FindView(ControllerContext, viewName, isMainPage: true);
            if (!viewResult.Success)
            {
                var absolute = $"/Areas/Admin/Views/Reports/{viewName}.cshtml";
                viewResult = _viewEngine.GetView(null, absolute, isMainPage: true);
            }
            if (!viewResult.Success || viewResult.View == null)
                throw new ArgumentNullException($"Could not find view: {viewName}");

            var vdd = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            {
                Model = model
            };

            // Pass through ViewBag collections to the view
            vdd["LogoDataUrl"] = ViewBag.LogoDataUrl;
            vdd["KeyIndicators"] = ViewBag.KeyIndicators;
            vdd["Insights"] = ViewBag.Insights;
            vdd["Recommendations"] = ViewBag.Recommendations;

            var vc = new ViewContext(ControllerContext, viewResult.View, vdd, TempData, sw, new HtmlHelperOptions());
            await viewResult.View.RenderAsync(vc);
            return sw.ToString();
        }
    }
}
