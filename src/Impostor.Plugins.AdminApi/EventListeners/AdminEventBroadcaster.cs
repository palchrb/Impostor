using System;
using Impostor.Api.Events;
using Impostor.Api.Events.Client;
using Impostor.Api.Events.Meeting;
using Impostor.Api.Events.Player;
using Impostor.Plugins.AdminApi.Services;

namespace Impostor.Plugins.AdminApi.EventListeners;

/// <summary>
/// Captures game/player/client events and pushes them to the EventBus (for SSE)
/// and ChatLog (for chat buffer).
/// </summary>
public class AdminEventBroadcaster : IEventListener
{
    private readonly EventBusService _bus;
    private readonly ChatLogService _chatLog;

    public AdminEventBroadcaster(EventBusService bus, ChatLogService chatLog)
    {
        _bus = bus;
        _chatLog = chatLog;
    }

    [EventListener]
    public void OnClientConnected(IClientConnectedEvent e)
    {
        var endpoint = e.Client.Connection?.EndPoint;
        _bus.Publish(new AdminEvent("client.connected", DateTime.UtcNow, new
        {
            clientId = e.Client.Id,
            name = e.Client.Name,
            ip = endpoint?.Address.ToString(),
            platform = e.Client.PlatformSpecificData.Platform.ToString(),
            version = e.Client.GameVersion.Value,
        }));
    }

    [EventListener]
    public void OnGameCreated(IGameCreatedEvent e)
    {
        _bus.Publish(new AdminEvent("game.created", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            hostName = e.Host?.Name,
            hostIp = e.Host?.Connection?.EndPoint?.Address.ToString(),
        }));
    }

    [EventListener]
    public void OnGameDestroyed(IGameDestroyedEvent e)
    {
        _bus.Publish(new AdminEvent("game.destroyed", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
        }));
    }

    [EventListener]
    public void OnGameStarted(IGameStartedEvent e)
    {
        _bus.Publish(new AdminEvent("game.started", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            playerCount = e.Game.PlayerCount,
        }));
    }

    [EventListener]
    public void OnGameEnded(IGameEndedEvent e)
    {
        _bus.Publish(new AdminEvent("game.ended", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            reason = e.GameOverReason.ToString(),
        }));
    }

    [EventListener]
    public void OnGameAlter(IGameAlterEvent e)
    {
        _bus.Publish(new AdminEvent("game.privacyChanged", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            isPublic = e.IsPublic,
        }));
    }

    [EventListener]
    public void OnPlayerJoined(IGamePlayerJoinedEvent e)
    {
        var endpoint = e.Player.Client.Connection?.EndPoint;
        _bus.Publish(new AdminEvent("player.joined", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            clientId = e.Player.Client.Id,
            name = e.Player.Client.Name,
            ip = endpoint?.Address.ToString(),
            isHost = e.Player.IsHost,
        }));
    }

    [EventListener]
    public void OnPlayerLeft(IGamePlayerLeftEvent e)
    {
        _bus.Publish(new AdminEvent("player.left", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            clientId = e.Player.Client.Id,
            name = e.Player.Client.Name,
        }));
    }

    [EventListener]
    public void OnPlayerChat(IPlayerChatEvent e)
    {
        var client = e.ClientPlayer.Client;
        var endpoint = client.Connection?.EndPoint;
        var code = e.Game.Code.Code;
        var playerName = e.PlayerControl.PlayerInfo?.PlayerName ?? client.Name;

        var chatMessage = new ChatMessage(
            Timestamp: DateTime.UtcNow,
            GameCode: code,
            ClientId: client.Id,
            PlayerName: playerName,
            Ip: endpoint?.Address.ToString(),
            Message: e.Message);

        _chatLog.Add(chatMessage);

        _bus.Publish(new AdminEvent("chat", DateTime.UtcNow, chatMessage));
    }

    [EventListener]
    public void OnPlayerMurder(IPlayerMurderEvent e)
    {
        _bus.Publish(new AdminEvent("player.murder", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            killerName = e.PlayerControl.PlayerInfo?.PlayerName,
            victimName = e.Victim.PlayerInfo?.PlayerName,
            result = e.Result.ToString(),
        }));
    }

    [EventListener]
    public void OnMeetingStarted(IMeetingStartedEvent e)
    {
        _bus.Publish(new AdminEvent("meeting.started", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
        }));
    }

    [EventListener]
    public void OnMeetingEnded(IMeetingEndedEvent e)
    {
        _bus.Publish(new AdminEvent("meeting.ended", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            exiledName = e.Exiled?.PlayerInfo?.PlayerName,
            isTie = e.IsTie,
        }));
    }
}
