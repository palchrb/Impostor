using Impostor.Api.Events;
using Impostor.Api.Plugins;
using Impostor.Plugins.AdminApi.Config;
using Impostor.Plugins.AdminApi.EventListeners;
using Impostor.Plugins.AdminApi.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Impostor.Plugins.AdminApi;

public class AdminApiStartup : IPluginStartup
{
    public void ConfigureHost(IHostBuilder host)
    {
        host.ConfigureServices((context, services) =>
        {
            services.Configure<AdminApiConfig>(context.Configuration.GetSection(AdminApiConfig.SectionName));
        });
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<StatsService>();

        services.AddSingleton<BanListService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<BanListService>>();
            var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AdminApiConfig>>().Value;
            var path = string.IsNullOrWhiteSpace(cfg.BanListPath) ? null : cfg.BanListPath;
            return new BanListService(logger, path);
        });

        services.AddSingleton<ChatLogService>(sp =>
        {
            var cfg = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AdminApiConfig>>().Value;
            return new ChatLogService(cfg.ChatLogBufferSize);
        });

        services.AddSingleton<EventBusService>();

        services.AddSingleton<IEventListener, StatsEventListener>();
        services.AddSingleton<IEventListener, BanEnforcementListener>();
        services.AddSingleton<IEventListener, AdminEventBroadcaster>();

        services.AddHostedService<AdminApiHost>();
    }
}
