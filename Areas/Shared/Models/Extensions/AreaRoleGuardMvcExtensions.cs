// File: Areas/Shared/Models/Extensions/AreaRoleGuardMvcExtensions.cs
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.DependencyInjection;

namespace JobPortal.Areas.Shared.Models.Extensions
{
    /// <summary>
    /// Applies AreaRoleGuardFilter to Admin (role=Admin), Recruiter (role=Recruiter),
    /// and JobSeeker (role=JobSeeker). All unauthenticated users are redirected to
    /// JobSeeker/Account/Login. Do NOT register the filter in DI; it takes ctor args
    /// and is constructed via TypeFilterAttribute.
    /// </summary>

    public static class AreaRoleGuardMvcExtensions
    {
        public static IMvcBuilder AddAreaRoleGuards(this IMvcBuilder mvc)
        {
            // ❌ Do NOT register AreaRoleGuardFilter in DI; it has string ctor args.
            // mvc.Services.AddScoped<AreaRoleGuardFilter>();

            mvc.Services.Configure<MvcOptions>(opts =>
            {
                // Admin area: require Admin, login area = JobSeeker
                opts.Conventions.Add(new AreaGuardConvention(
                    areaName: "Admin",
                    requiredRole: "Admin",
                    loginArea: "JobSeeker"));

                // Recruiter area: require Recruiter, login area = JobSeeker (no Recruiter login page)
                opts.Conventions.Add(new AreaGuardConvention(
                    areaName: "Recruiter",
                    requiredRole: "Recruiter",
                    loginArea: "JobSeeker"));


                // ✅ JobSeeker area: require JobSeeker, login area = JobSeeker
                opts.Conventions.Add(new AreaGuardConvention(
                    areaName: "JobSeeker",
                    requiredRole: "JobSeeker",
                    loginArea: "JobSeeker"));

            });

            return mvc;
        }

        private sealed class AreaGuardConvention : IApplicationModelConvention
        {
            private readonly string _areaName;
            private readonly string _requiredRole;
            private readonly string _loginArea;

            public AreaGuardConvention(string areaName, string requiredRole, string loginArea)
            {
                _areaName = areaName;
                _requiredRole = requiredRole;
                _loginArea = loginArea;
            }

            public void Apply(ApplicationModel application)
            {
                foreach (var controller in application.Controllers)
                {
                    var isArea =
                        controller.Attributes.Any(a => a is AreaAttribute aa && aa.RouteValue == _areaName) ||
                        (controller.RouteValues.TryGetValue("area", out var area) && area == _areaName);

                    if (!isArea) continue;

                    controller.Filters.Add(new TypeFilterAttribute(typeof(AreaRoleGuardFilter))
                    {
                        Arguments = new object[] { _requiredRole, _loginArea }
                    });
                }
            }
        }
    }
}
