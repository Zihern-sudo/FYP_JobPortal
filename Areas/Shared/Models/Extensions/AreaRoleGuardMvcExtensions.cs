// File: Areas/Shared/Models/Extensions/AreaRoleGuardMvcExtensions.cs
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.DependencyInjection;

namespace JobPortal.Areas.Shared.Models.Extensions
{
    /// <summary>
    /// Applies AreaRoleGuardFilter to Admin (role=Admin) and Recruiter (role=Recruiter).
    /// Both redirect to JobSeeker/Account/Login. DO NOT register the filter in DI because it
    /// has ctor string parameters; TypeFilterAttribute will construct it with Arguments.
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
