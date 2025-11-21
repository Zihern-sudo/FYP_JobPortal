using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace JobPortal.Areas.Services
{
    public class JobSeekerNotificationBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;

        public JobSeekerNotificationBackgroundService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var notificationService = scope.ServiceProvider.GetRequiredService<JobSeekerNotificationService>();
                    await notificationService.CheckFavoritedJobExpiryAsync();
                    // You can also check messages if you want automatic message notifications
                    // await notificationService.CheckUnreadRecruiterMessagesAsync(userId); 
                }

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken); // run every 1 hour
            }
        }
    }
}
