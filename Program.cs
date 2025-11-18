using Microsoft.EntityFrameworkCore;
using JobPortal.Areas.Shared.Models;
using JobPortal.Services;
using JobPortal.Areas.Shared.Models.Extensions; // AddAreaRoleGuards()
using QuestPDF.Infrastructure;
using OpenAI;


// AI namespaces
using JobPortal.Areas.Shared.Options;
using JobPortal.Areas.Shared.AI;

var builder = WebApplication.CreateBuilder(args);

// DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection"))));

// MVC + area role guards
builder.Services
    .AddControllersWithViews()
    .AddAreaRoleGuards();

builder.Services.AddSession(options => { options.IdleTimeout = TimeSpan.FromMinutes(30); });

builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// ==============================
// OpenAI config + clients (scoring + parsing)
// ==============================
builder.Services
    .AddOptions<OpenAIOptions>()
    .Bind(builder.Configuration.GetSection(OpenAIOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "OpenAI:ApiKey is required.")
    .ValidateOnStart();

builder.Services.AddHttpClient<IOpenAIClient, OpenAIClient>();
builder.Services.AddScoped<ILanguageService, LanguageService>();
builder.Services.AddScoped<IScoringService, ScoringService>();
builder.Services.AddScoped<AiOrchestrator>();

// --- REMOVE Gemini ---
// (Deleted GeminiOptions, GeminiClient, GeminiResumeParser registrations)

// --- ADD OpenAI-based resume parser ---
builder.Services.AddScoped<OpenAIResumeParser>();

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

// Routes
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
