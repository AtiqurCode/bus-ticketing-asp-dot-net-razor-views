using System.Collections.Concurrent;

namespace BusTicketing.Services.Bookings;

/// <summary>
/// In-process fan-out so every open seat map for a trip refreshes the moment a
/// seat is held, released or booked — no SignalR hub, just Blazor Server circuits
/// reacting to an event. Fine for a single-node deployment.
/// </summary>
public sealed class SeatMapBroadcaster
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Func<Task>>> _subscribers = new();

    public IDisposable Subscribe(Guid tripId, Func<Task> onChanged)
    {
        var forTrip = _subscribers.GetOrAdd(tripId, _ => new ConcurrentDictionary<Guid, Func<Task>>());
        var key = Guid.NewGuid();
        forTrip[key] = onChanged;
        return new Subscription(() =>
        {
            if (_subscribers.TryGetValue(tripId, out var map))
            {
                map.TryRemove(key, out _);
                if (map.IsEmpty)
                    _subscribers.TryRemove(tripId, out _);
            }
        });
    }

    public void Notify(Guid tripId)
    {
        if (!_subscribers.TryGetValue(tripId, out var forTrip))
            return;

        foreach (var callback in forTrip.Values)
        {
            _ = InvokeSafely(callback);
        }
    }

    private static async Task InvokeSafely(Func<Task> callback)
    {
        try
        {
            await callback();
        }
        catch
        {
            // A dead circuit — its unsubscribe just hasn't run yet. Ignore.
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
