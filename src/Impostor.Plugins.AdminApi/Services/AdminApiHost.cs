using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Impostor.Api.Games;
using Impostor.Api.Games.Managers;
using Impostor.Api.Innersloth;
using Impostor.Api.Net;
using Impostor.Api.Net.Manager;
using Impostor.Plugins.AdminApi.Config;
using Impostor.Plugins.AdminApi.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Impostor.Plugins.AdminApi.Services;

public class AdminApiHost : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private readonly ILogger<AdminApiHost> _logger;
    private readonly AdminApiConfig _config;
    private readonly IGameManager _gameManager;
    private readonly IClientManager _clientManager;
    private readonly StatsService _stats;
    private readonly BanListService _banList;
    private readonly ChatLogService _chatLog;
    private readonly EventBusService _eventBus;
    private HttpListener? _listener;

    public AdminApiHost(
        ILogger<AdminApiHost> logger,
        IOptions<AdminApiConfig> config,
        IGameManager gameManager,
        IClientManager clientManager,
        StatsService stats,
        BanListService banList,
        ChatLogService chatLog,
        EventBusService eventBus)
    {
        _logger = logger;
        _config = config.Value;
        _gameManager = gameManager;
        _clientManager = clientManager;
        _stats = stats;
        _banList = banList;
        _chatLog = chatLog;
        _eventBus = eventBus;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("Admin API is disabled in config.");
            return;
        }

        // HttpListener uses "+" to bind to all interfaces (0.0.0.0 is not valid for HttpListener prefix)
        var host = _config.ListenIp == "0.0.0.0" ? "+" : _config.ListenIp;
        var prefix = $"http://{host}:{_config.ListenPort}/";

        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);

        try
        {
            _listener.Start();
            _logger.LogInformation("Admin API listening on {Prefix}", prefix);
        }
        catch (HttpListenerException ex)
        {
            _logger.LogError(ex, "Failed to start Admin API listener on {Prefix}", prefix);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }

            _ = Task.Run(() => HandleRequestAsync(context, stoppingToken), stoppingToken);
        }

        _listener.Stop();
        _listener.Close();
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken stoppingToken)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            if (!string.IsNullOrEmpty(_config.ApiKey))
            {
                var provided = request.Headers["X-Admin-Key"];
                if (provided != _config.ApiKey)
                {
                    await WriteJsonAsync(response, 401, new ErrorDto("Unauthorized"));
                    return;
                }
            }

            var path = request.Url?.AbsolutePath ?? string.Empty;
            var method = request.HttpMethod;

            _logger.LogDebug("Admin API {Method} {Path}", method, path);

            // --- Read-only ---
            if (method == "GET" && path == "/admin/stats")
            {
                await HandleStatsAsync(response);
                return;
            }

            if (method == "GET" && path == "/admin/games")
            {
                await HandleGamesListAsync(response);
                return;
            }

            var gameDetailMatch = Regex.Match(path, @"^/admin/games/([^/]+)$");
            if (method == "GET" && gameDetailMatch.Success)
            {
                await HandleGameDetailAsync(response, gameDetailMatch.Groups[1].Value);
                return;
            }

            if (method == "GET" && path == "/admin/clients")
            {
                await HandleClientsListAsync(response);
                return;
            }

            // --- Moderation ---
            var disconnectMatch = Regex.Match(path, @"^/admin/clients/(\d+)/disconnect$");
            if (method == "POST" && disconnectMatch.Success)
            {
                await HandleDisconnectClientAsync(response, int.Parse(disconnectMatch.Groups[1].Value));
                return;
            }

            var kickMatch = Regex.Match(path, @"^/admin/games/([^/]+)/players/(\d+)/kick$");
            if (method == "POST" && kickMatch.Success)
            {
                await HandleKickPlayerAsync(response, kickMatch.Groups[1].Value, int.Parse(kickMatch.Groups[2].Value));
                return;
            }

            // --- Ban list ---
            if (method == "GET" && path == "/admin/bans")
            {
                await WriteJsonAsync(response, 200, _banList.GetAll());
                return;
            }

            if (method == "POST" && path == "/admin/bans")
            {
                await HandleAddBanAsync(request, response);
                return;
            }

            var banDeleteMatch = Regex.Match(path, @"^/admin/bans/(.+)$");
            if (method == "DELETE" && banDeleteMatch.Success)
            {
                await HandleRemoveBanAsync(response, Uri.UnescapeDataString(banDeleteMatch.Groups[1].Value));
                return;
            }

            // --- Chat log ---
            if (method == "GET" && path == "/admin/chat/recent")
            {
                var limit = int.TryParse(request.QueryString["limit"], out var l) ? l : 100;
                await WriteJsonAsync(response, 200, _chatLog.GetRecent(limit));
                return;
            }

            var chatByGameMatch = Regex.Match(path, @"^/admin/chat/([^/]+)$");
            if (method == "GET" && chatByGameMatch.Success)
            {
                var limit = int.TryParse(request.QueryString["limit"], out var l) ? l : 100;
                await WriteJsonAsync(response, 200, _chatLog.GetByGame(chatByGameMatch.Groups[1].Value.ToUpperInvariant(), limit));
                return;
            }

            // --- SSE events ---
            if (method == "GET" && path == "/admin/events")
            {
                await HandleEventStreamAsync(response, stoppingToken);
                return;
            }

            await WriteJsonAsync(response, 404, new ErrorDto($"Not found: {method} {path}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Admin API request");
            try
            {
                await WriteJsonAsync(context.Response, 500, new ErrorDto("Internal server error"));
            }
            catch
            {
                // Ignore
            }
        }
    }

    private Task HandleStatsAsync(HttpListenerResponse response)
    {
        var games = _gameManager.Games.ToList();
        var totalPlayers = games.Sum(g => g.PlayerCount);

        var dto = new StatsDto(
            StartedAt: _stats.StartedAt,
            UptimeSeconds: (DateTime.UtcNow - _stats.StartedAt).TotalSeconds,
            ActiveGames: games.Count,
            ConnectedClients: _clientManager.Clients.Count(),
            PublicGames: games.Count(g => g.IsPublic),
            PrivateGames: games.Count(g => !g.IsPublic),
            TotalPlayers: totalPlayers);

        return WriteJsonAsync(response, 200, dto);
    }

    private Task HandleGamesListAsync(HttpListenerResponse response)
    {
        var list = _gameManager.Games.Select(MapGameSummary).ToList();
        return WriteJsonAsync(response, 200, list);
    }

    private Task HandleGameDetailAsync(HttpListenerResponse response, string codeStr)
    {
        var code = TryParseGameCode(codeStr);
        if (code == null)
        {
            return WriteJsonAsync(response, 400, new ErrorDto("Invalid game code"));
        }

        var game = _gameManager.Find(code.Value);
        if (game == null)
        {
            return WriteJsonAsync(response, 404, new ErrorDto("Game not found"));
        }

        var dto = new GameDetailDto(
            Summary: MapGameSummary(game),
            Players: game.Players.Select(p => MapPlayer(p)).ToList());

        return WriteJsonAsync(response, 200, dto);
    }

    private Task HandleClientsListAsync(HttpListenerResponse response)
    {
        var list = _clientManager.Clients.Select(c =>
        {
            var endpoint = c.Connection?.EndPoint;
            var player = c.Player;
            return new ClientDto(
                Id: c.Id,
                Name: c.Name,
                Ip: endpoint?.Address.ToString(),
                Port: endpoint?.Port,
                Platform: c.PlatformSpecificData.Platform.ToString(),
                PlatformName: c.PlatformSpecificData.PlatformName,
                GameVersion: c.GameVersion.Value,
                Language: c.Language.ToString(),
                ChatMode: c.ChatMode.ToString(),
                PingMs: c.Connection?.AveragePing,
                GameCode: player?.Game.Code.Code,
                InGame: player != null);
        }).ToList();

        return WriteJsonAsync(response, 200, list);
    }

    private async Task HandleDisconnectClientAsync(HttpListenerResponse response, int clientId)
    {
        var client = _clientManager.Clients.FirstOrDefault(c => c.Id == clientId);
        if (client == null)
        {
            await WriteJsonAsync(response, 404, new ErrorDto("Client not found"));
            return;
        }

        await client.DisconnectAsync(DisconnectReason.Custom, "Disconnected by server administrator");
        await WriteJsonAsync(response, 200, new { ok = true, clientId });
    }

    private async Task HandleKickPlayerAsync(HttpListenerResponse response, string codeStr, int playerClientId)
    {
        var code = TryParseGameCode(codeStr);
        if (code == null)
        {
            await WriteJsonAsync(response, 400, new ErrorDto("Invalid game code"));
            return;
        }

        var game = _gameManager.Find(code.Value);
        if (game == null)
        {
            await WriteJsonAsync(response, 404, new ErrorDto("Game not found"));
            return;
        }

        var player = game.GetClientPlayer(playerClientId);
        if (player == null)
        {
            await WriteJsonAsync(response, 404, new ErrorDto("Player not found in game"));
            return;
        }

        await player.KickAsync();
        await WriteJsonAsync(response, 200, new { ok = true, code = game.Code.Code, clientId = playerClientId });
    }

    private async Task HandleAddBanAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
        var body = await reader.ReadToEndAsync();

        AddBanDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<AddBanDto>(body, JsonOptions);
        }
        catch (JsonException)
        {
            await WriteJsonAsync(response, 400, new ErrorDto("Invalid JSON body"));
            return;
        }

        if (dto == null || string.IsNullOrWhiteSpace(dto.Ip))
        {
            await WriteJsonAsync(response, 400, new ErrorDto("Missing 'ip' field"));
            return;
        }

        if (!IPAddress.TryParse(dto.Ip, out _))
        {
            await WriteJsonAsync(response, 400, new ErrorDto("Invalid IP address"));
            return;
        }

        var added = _banList.Add(dto.Ip, dto.Reason);

        // Optionally kick all currently connected clients from that IP
        foreach (var client in _clientManager.Clients.ToArray())
        {
            var endpoint = client.Connection?.EndPoint;
            if (endpoint != null && endpoint.Address.ToString() == dto.Ip)
            {
                try
                {
                    await client.DisconnectAsync(DisconnectReason.Banned);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to disconnect banned client {ClientId}", client.Id);
                }
            }
        }

        await WriteJsonAsync(response, added ? 201 : 200, new { ok = true, added, ip = dto.Ip });
    }

    private async Task HandleRemoveBanAsync(HttpListenerResponse response, string ip)
    {
        var removed = _banList.Remove(ip);
        await WriteJsonAsync(response, removed ? 200 : 404, new { ok = removed, ip });
    }

    private async Task HandleEventStreamAsync(HttpListenerResponse response, CancellationToken stoppingToken)
    {
        response.StatusCode = 200;
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        response.SendChunked = true;

        // Keep connection open
        response.KeepAlive = true;

        await using var subscription = _eventBus.Subscribe();

        // Send initial hello
        await WriteSseEventAsync(response, "hello", new { subscriptionId = subscription.Id });

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var evt = await subscription.Reader.ReadAsync(stoppingToken);
                    await WriteSseEventAsync(response, evt.Type, evt);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Client disconnected
                    break;
                }
            }
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch
            {
                // Ignore
            }
        }
    }

    private static async Task WriteSseEventAsync(HttpListenerResponse response, string eventType, object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        var payload = $"event: {eventType}\ndata: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(payload);
        await response.OutputStream.WriteAsync(bytes);
        await response.OutputStream.FlushAsync();
    }

    private static GameSummaryDto MapGameSummary(IGame game)
    {
        var host = game.Host;
        var hostEndpoint = host?.Client.Connection?.EndPoint;

        return new GameSummaryDto(
            Code: game.Code.Code,
            GameId: game.Code.Value,
            HostName: host?.Client.Name,
            DisplayName: game.DisplayName,
            PlayerCount: game.PlayerCount,
            MaxPlayers: game.Options.MaxPlayers,
            State: game.GameState.ToString(),
            IsPublic: game.IsPublic,
            NumImpostors: game.Options.NumImpostors,
            MapId: (int)game.Options.Map,
            LanguageKeywords: (long)game.Options.Keywords,
            GameMode: game.Options.GameMode.ToString(),
            HostIp: hostEndpoint?.Address.ToString(),
            HostClientId: host?.Client.Id ?? -1);
    }

    private static PlayerDto MapPlayer(IClientPlayer player)
    {
        var client = player.Client;
        var endpoint = client.Connection?.EndPoint;
        var character = player.Character;
        var info = character?.PlayerInfo;

        return new PlayerDto(
            ClientId: client.Id,
            Name: client.Name,
            Ip: endpoint?.Address.ToString(),
            Port: endpoint?.Port,
            Platform: client.PlatformSpecificData.Platform.ToString(),
            PlatformName: client.PlatformSpecificData.PlatformName,
            GameVersion: client.GameVersion.Value,
            Language: client.Language.ToString(),
            ChatMode: client.ChatMode.ToString(),
            PingMs: client.Connection?.AveragePing,
            IsHost: player.IsHost,
            Limbo: player.Limbo.ToString(),
            RoleType: info?.RoleType?.ToString(),
            IsDead: info?.IsDead,
            PlayerId: character?.PlayerId);
    }

    private static GameCode? TryParseGameCode(string codeStr)
    {
        try
        {
            if (codeStr.Length >= 4 && !int.TryParse(codeStr, out _))
            {
                return GameCode.From(codeStr.ToUpperInvariant());
            }

            if (int.TryParse(codeStr, out var intCode))
            {
                return new GameCode(intCode);
            }
        }
        catch
        {
            // Fall through
        }

        return null;
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object body)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(body, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
}

public record AddBanDto(string Ip, string? Reason);
