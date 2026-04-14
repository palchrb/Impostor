using Impostor.Api.Events;
using Impostor.Api.Plugins;
using Impostor.Plugins.AdminApi.Config;
using Impostor.Plugins.AdminApi.EventListeners;
using Impostor.Plugins.AdminApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        services.AddSingleton<IEventListener, StatsEventListener>();
        services.AddHostedService<AdminApiHost>();
    }
}
