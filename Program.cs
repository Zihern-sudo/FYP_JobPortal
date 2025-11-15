using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using JobPortal.Services;
using JobPortal.Areas.Shared.Models.Extensions; // ⬅ AddAreaRoleGuards()
using QuestPDF.Infrastructure;
using OpenAI;


var builder = WebApplication.CreateBuilder(args);

// DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection"))));

// MVC + area role guards (Admin + Recruiter)
builder.Services
    .AddControllersWithViews()
    .AddAreaRoleGuards(); // ⬅ applies guards to Admin and Recruiter areas

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
});

builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<INotificationService, NotificationService>(); // ⬅ register notification service

builder.Services.AddScoped<JobPortal.Services.ChatbotService>();



var app = builder.Build();

app.UseSession();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

QuestPDF.Settings.License = LicenseType.Community;

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

// Root "/" → Public landing page
app.MapControllerRoute(
    name: "public_root",
    pattern: "",
    defaults: new { area = "Public", controller = "Home", action = "Index" });

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}",
    defaults: new { area = "JobSeeker" });

app.Run();
