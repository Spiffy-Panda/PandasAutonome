using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autonome.Core.Simulation;
using Autonome.Core.World;

namespace Autonome.Web;

/// <summary>
/// Manages WebSocket connections and broadcasts tick events to all connected clients.
/// </summary>
public class WebSocketHub
{
    private readonly List<ConnectedClient> _clients = [];
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public void AddClient(ConnectedClient client)
    {
        lock (_lock) _clients.Add(client);
        Console.WriteLine($"WebSocket client connected ({_clients.Count} total)");
    }

    public void RemoveClient(ConnectedClient client)
    {
        lock (_lock) _clients.Remove(client);
        Console.WriteLine($"WebSocket client disconnected ({_clients.Count} total)");
    }

    public void BroadcastTick(TickResult result, WorldState world)
    {
        List<ConnectedClient> snapshot;
        lock (_lock) snapshot = [.. _clients];

        if (snapshot.Count == 0) return;

        // Serialize full tick data
        var fullMessage = JsonSerializer.Serialize(new
        {
            type = "tick",
            tick = result.Tick,
            gameTime = result.GameTime,
            gameDay = world.Clock.GameDay,
            gameHour = Math.Round(world.Clock.GameHour, 2),
            events = result.Events.Select(e => new
            {
                entityId = e.AutonomeId,
                actionId = e.ActionId,
                score = Math.Round(e.Score, 4),
                location = e.Location,
                embodied = e.Embodied
            })
        }, JsonOpts);

        // Fire-and-forget broadcast to all clients
        foreach (var client in snapshot)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    string message = fullMessage;

                    // If client is bound to an entity, filter events by visibility
                    if (client.EntityId != null)
                    {
                        var entityLoc = world.Locations.GetLocation(client.EntityId);
                        if (entityLoc != null)
                        {
                            var visibleEvents = result.Events
                                .Where(e => e.Location != null && IsLocationVisible(entityLoc, e.Location, world))
                                .Select(e => new
                                {
                                    entityId = e.AutonomeId,
                                    actionId = e.ActionId,
                                    score = Math.Round(e.Score, 4),
                                    location = e.Location,
                                    embodied = e.Embodied
                                });

                            message = JsonSerializer.Serialize(new
                            {
                                type = "tick",
                                tick = result.Tick,
                                gameTime = result.GameTime,
                                gameDay = world.Clock.GameDay,
                                gameHour = Math.Round(world.Clock.GameHour, 2),
                                viewerLocation = entityLoc,
                                events = visibleEvents
                            }, JsonOpts);
                        }
                    }

                    await client.SendAsync(message);
                }
                catch
                {
                    // Client probably disconnected — will be cleaned up
                }
            });
        }
    }

    private static bool IsLocationVisible(string viewerLocation, string targetLocation, WorldState world)
    {
        if (viewerLocation == targetLocation) return true;

        // Adjacent locations are visible
        var def = world.Locations.GetDefinition(viewerLocation);
        if (def != null)
        {
            foreach (var edge in def.ConnectedTo)
            {
                if (edge.Target == targetLocation) return true;
            }
        }
        return false;
    }
}

/// <summary>
/// Represents a connected WebSocket client. Optionally bound to an entity for filtered views.
/// </summary>
public class ConnectedClient
{
    private readonly WebSocket _socket;

    public string? EntityId { get; set; }
    public string? Token { get; set; }

    public ConnectedClient(WebSocket socket)
    {
        _socket = socket;
    }

    public async Task SendAsync(string json)
    {
        if (_socket.State != WebSocketState.Open) return;
        var bytes = Encoding.UTF8.GetBytes(json);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
