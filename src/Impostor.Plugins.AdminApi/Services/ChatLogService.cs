using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Impostor.Plugins.AdminApi.Services;

public record ChatMessage(
    DateTime Timestamp,
    string GameCode,
    int ClientId,
    string PlayerName,
    string? Ip,
    string Message);

public class ChatLogService
{
    private readonly int _capacity;
    private readonly ConcurrentQueue<ChatMessage> _messages = new();

    public ChatLogService(int capacity = 1000)
    {
        _capacity = capacity;
    }

    public void Add(ChatMessage message)
    {
        _messages.Enqueue(message);
        while (_messages.Count > _capacity && _messages.TryDequeue(out _))
        {
            // Trim to capacity
        }
    }

    public IEnumerable<ChatMessage> GetRecent(int limit = 100)
    {
        return _messages.TakeLast(limit);
    }

    public IEnumerable<ChatMessage> GetByGame(string code, int limit = 100)
    {
        return _messages.Where(m => m.GameCode == code).TakeLast(limit);
    }
}
