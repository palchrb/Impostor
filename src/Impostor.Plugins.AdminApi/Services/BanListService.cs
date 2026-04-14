using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Impostor.Plugins.AdminApi.Services;

public record BanEntry(
    [property: JsonPropertyName("ip")] string Ip,
    [property: JsonPropertyName("reason")] string? Reason,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt);

public class BanListService
{
    private readonly ILogger<BanListService> _logger;
    private readonly ConcurrentDictionary<string, BanEntry> _bans = new();
    private readonly string? _persistPath;
    private readonly object _fileLock = new();

    public BanListService(ILogger<BanListService> logger, string? persistPath)
    {
        _logger = logger;
        _persistPath = persistPath;
        Load();
    }

    public IEnumerable<BanEntry> GetAll() => _bans.Values.ToArray();

    public bool IsBanned(IPAddress ip) => _bans.ContainsKey(ip.ToString());

    public bool Add(string ip, string? reason)
    {
        var entry = new BanEntry(ip, reason, DateTime.UtcNow);
        var added = _bans.TryAdd(ip, entry);
        if (added)
        {
            _logger.LogInformation("IP {Ip} banned (reason: {Reason})", ip, reason ?? "<none>");
            Save();
        }

        return added;
    }

    public bool Remove(string ip)
    {
        var removed = _bans.TryRemove(ip, out _);
        if (removed)
        {
            _logger.LogInformation("IP {Ip} unbanned", ip);
            Save();
        }

        return removed;
    }

    private void Load()
    {
        if (string.IsNullOrEmpty(_persistPath) || !File.Exists(_persistPath))
        {
            return;
        }

        try
        {
            lock (_fileLock)
            {
                var json = File.ReadAllText(_persistPath);
                var list = JsonSerializer.Deserialize<List<BanEntry>>(json);
                if (list == null)
                {
                    return;
                }

                foreach (var entry in list)
                {
                    _bans[entry.Ip] = entry;
                }

                _logger.LogInformation("Loaded {Count} bans from {Path}", list.Count, _persistPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load ban list from {Path}", _persistPath);
        }
    }

    private void Save()
    {
        if (string.IsNullOrEmpty(_persistPath))
        {
            return;
        }

        try
        {
            lock (_fileLock)
            {
                var dir = Path.GetDirectoryName(_persistPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(_bans.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_persistPath, json);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist ban list to {Path} (continuing with in-memory only)", _persistPath);
        }
    }
}
