using System.Threading.Tasks;
using Impostor.Api.Plugins;
using Microsoft.Extensions.Logging;

namespace Impostor.Plugins.AdminApi;

[ImpostorPlugin("me.vibb.impostor.adminapi")]
public class AdminApiPlugin : PluginBase
{
    private readonly ILogger<AdminApiPlugin> _logger;

    public AdminApiPlugin(ILogger<AdminApiPlugin> logger)
    {
        _logger = logger;
    }

    public override ValueTask EnableAsync()
    {
        _logger.LogInformation("Admin API plugin enabled.");
        return default;
    }

    public override ValueTask DisableAsync()
    {
        _logger.LogInformation("Admin API plugin disabled.");
        return default;
    }
}
