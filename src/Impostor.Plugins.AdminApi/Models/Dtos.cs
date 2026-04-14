using System;
using System.Collections.Generic;

namespace Impostor.Plugins.AdminApi.Models;

public record StatsDto(
    DateTime StartedAt,
    double UptimeSeconds,
    int ActiveGames,
    int ConnectedClients,
    int PublicGames,
    int PrivateGames,
    int TotalPlayers);

public record GameSummaryDto(
    string Code,
    int GameId,
    string? HostName,
    string? DisplayName,
    int PlayerCount,
    int MaxPlayers,
    string State,
    bool IsPublic,
    int NumImpostors,
    int MapId,
    long LanguageKeywords,
    string GameMode,
    string? HostIp,
    int HostClientId);

public record PlayerDto(
    int ClientId,
    string Name,
    string? Ip,
    int? Port,
    string Platform,
    string? PlatformName,
    int GameVersion,
    string Language,
    string ChatMode,
    float? PingMs,
    bool IsHost,
    string? Limbo,
    string? RoleType,
    bool? IsDead,
    byte? PlayerId);

public record GameDetailDto(
    GameSummaryDto Summary,
    List<PlayerDto> Players);

public record ClientDto(
    int Id,
    string Name,
    string? Ip,
    int? Port,
    string Platform,
    string? PlatformName,
    int GameVersion,
    string Language,
    string ChatMode,
    float? PingMs,
    string? GameCode,
    bool InGame);

public record ErrorDto(string Error);
