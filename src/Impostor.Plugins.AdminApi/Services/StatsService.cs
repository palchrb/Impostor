using System;
using System.Threading;

namespace Impostor.Plugins.AdminApi.Services;

public class StatsService
{
    private long _totalGamesCreated;
    private long _totalGamesDestroyed;
    private long _totalClientsConnected;
    private long _totalPlayerJoins;

    public DateTime StartedAt { get; } = DateTime.UtcNow;

    public long TotalGamesCreated => Interlocked.Read(ref _totalGamesCreated);

    public long TotalGamesDestroyed => Interlocked.Read(ref _totalGamesDestroyed);

    public long TotalClientsConnected => Interlocked.Read(ref _totalClientsConnected);

    public long TotalPlayerJoins => Interlocked.Read(ref _totalPlayerJoins);

    public void IncrementGamesCreated() => Interlocked.Increment(ref _totalGamesCreated);

    public void IncrementGamesDestroyed() => Interlocked.Increment(ref _totalGamesDestroyed);

    public void IncrementClientsConnected() => Interlocked.Increment(ref _totalClientsConnected);

    public void IncrementPlayerJoins() => Interlocked.Increment(ref _totalPlayerJoins);
}
