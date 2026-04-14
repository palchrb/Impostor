using Impostor.Api.Events;
using Impostor.Api.Games;
using Impostor.Api.Innersloth;
using Impostor.Plugins.AdminApi.Services;
using Microsoft.Extensions.Logging;

namespace Impostor.Plugins.AdminApi.EventListeners;

public class BanEnforcementListener : IEventListener
{
    private readonly ILogger<BanEnforcementListener> _logger;
    private readonly BanListService _banList;

    public BanEnforcementListener(ILogger<BanEnforcementListener> logger, BanListService banList)
    {
        _logger = logger;
        _banList = banList;
    }

    [EventListener]
    public void OnPlayerJoining(IGamePlayerJoiningEvent e)
    {
        var client = e.Player.Client;
        var endpoint = client.Connection?.EndPoint;
        if (endpoint == null)
        {
            return;
        }

        if (_banList.IsBanned(endpoint.Address))
        {
            _logger.LogInformation(
                "Rejecting join from banned IP {Ip} (client {Name})",
                endpoint.Address,
                client.Name);

            e.JoinResult = GameJoinResult.FromError(GameJoinError.Banned);
        }
    }
}
