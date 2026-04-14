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
    private HttpListener? _listener;

    public AdminApiHost(
        ILogger<AdminApiHost> logger,
        IOptions<AdminApiConfig> config,
        IGameManager gameManager,
        IClientManager clientManager,
        StatsService stats)
    {
        _logger = logger;
        _config = config.Value;
        _gameManager = gameManager;
        _clientManager = clientManager;
        _stats = stats;
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
            _logger.LogError(ex, "Failed to start Admin API listener on {Prefix}. On Linux the ListenIp should normally be 127.0.0.1 and the container must have the port bound.", prefix);
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

            _ = Task.Run(() => HandleRequestAsync(context), stoppingToken);
        }

        _listener.Stop();
        _listener.Close();
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            // Optional API key auth
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

    private static PlayerDto MapPlayer(Api.Net.IClientPlayer player)
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
            // Try as 6-letter Among Us game code first (e.g. "ABCDEF")
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
