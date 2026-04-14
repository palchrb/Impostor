using Impostor.Api.Events;
using Impostor.Api.Events.Client;
using Impostor.Plugins.AdminApi.Services;

namespace Impostor.Plugins.AdminApi.EventListeners;

public class StatsEventListener : IEventListener
{
    private readonly StatsService _stats;

    public StatsEventListener(StatsService stats)
    {
        _stats = stats;
    }

    [EventListener]
    public void OnGameCreated(IGameCreatedEvent e) => _stats.IncrementGamesCreated();

    [EventListener]
    public void OnGameDestroyed(IGameDestroyedEvent e) => _stats.IncrementGamesDestroyed();

    [EventListener]
    public void OnClientConnected(IClientConnectedEvent e) => _stats.IncrementClientsConnected();

    [EventListener]
    public void OnPlayerJoined(IGamePlayerJoinedEvent e) => _stats.IncrementPlayerJoins();
}
