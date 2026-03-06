using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Autonome.Core.Model;
using Autonome.Core.Runtime;
using Autonome.Core.Simulation;
using Autonome.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<InteractiveSimulation>();
builder.Services.AddSingleton<ExternalSlotManager>();
builder.Services.AddSingleton<WebSocketHub>();
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(b => b.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();
app.UseCors();
app.UseWebSockets();

var jsonOpts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

// --- Load world ---
var sim = app.Services.GetRequiredService<InteractiveSimulation>();
var hub = app.Services.GetRequiredService<WebSocketHub>();
var slots = app.Services.GetRequiredService<ExternalSlotManager>();

// Wire up WebSocket broadcasts
sim.OnTickCompleted += result => hub.BroadcastTick(result, sim.World);

string dataPath = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "worlds/coastal_city";

// Resolve relative paths from the AutonomeSimulator project root
if (!Path.IsPathRooted(dataPath))
{
    // Walk up from bin/Debug/net8.0 → src/Autonome.Web → src → AutonomeSimulator root
    var projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var candidate = Path.Combine(projectRoot, dataPath);
    if (Directory.Exists(candidate))
        dataPath = candidate;
}

Console.WriteLine($"=== Autonome Interactive Server ===");
Console.WriteLine($"Data path: {dataPath}");
sim.Load(dataPath);

// ===================== SIMULATION CONTROL =====================

app.MapGet("/api/simulation/status", () => new
{
    tick = sim.World.Clock.Tick,
    gameTime = sim.World.Clock.FormatGameTime(),
    gameDay = sim.World.Clock.GameDay,
    gameHour = sim.World.Clock.GameHour,
    isAutoAdvancing = sim.IsAutoAdvancing,
    ticksPerSecond = sim.TicksPerSecond,
    entityCount = sim.Profiles.Count,
    externalEntities = sim.ExternalActions.ExternalEntityIds.ToList()
});

app.MapPost("/api/simulation/tick", () =>
{
    var result = sim.AdvanceTick();
    return Results.Ok(SerializeTickResult(result));
});

app.MapPost("/api/simulation/auto", (AutoAdvanceRequest req) =>
{
    float tps = Math.Clamp(req.TicksPerSecond, 0.1f, 100f);
    sim.StartAutoAdvance(tps);
    return Results.Ok(new { status = "auto-advancing", ticksPerSecond = tps });
});

app.MapPost("/api/simulation/pause", () =>
{
    sim.StopAutoAdvance();
    return Results.Ok(new { status = "paused", tick = sim.World.Clock.Tick });
});

// ===================== WORLD STATE =====================

app.MapGet("/api/world/state", () =>
{
    var locations = new Dictionary<string, object>();
    foreach (var locId in sim.World.Locations.AllLocationIds)
    {
        var def = sim.World.Locations.GetDefinition(locId);
        var entities = sim.World.Locations.GetEntitiesAtLocation(locId);
        var locProps = sim.World.LocationStates.Get(locId);

        locations[locId] = new
        {
            displayName = def?.DisplayName,
            tags = def?.Tags,
            entityCount = entities.Count,
            entities = entities.ToList(),
            properties = locProps?.ToDictionary(p => p.Key, p => new { p.Value.Value, p.Value.Min, p.Value.Max }),
            connectedTo = def?.ConnectedTo.Select(e => new { target = e.Target, cost = e.Cost })
        };
    }

    return Results.Ok(new
    {
        tick = sim.World.Clock.Tick,
        gameTime = sim.World.Clock.FormatGameTime(),
        gameDay = sim.World.Clock.GameDay,
        gameHour = sim.World.Clock.GameHour,
        locations
    });
});

app.MapGet("/api/world/tick", () =>
{
    var recentEvents = sim.RecentEvents
        .Where(e => e.Tick > sim.World.Clock.Tick - 10)
        .Select(e => new { e.Tick, e.AutonomeId, e.ActionId, e.Score, e.Location })
        .ToList();

    return Results.Ok(new
    {
        tick = sim.World.Clock.Tick,
        gameTime = sim.World.Clock.FormatGameTime(),
        gameDay = sim.World.Clock.GameDay,
        gameHour = sim.World.Clock.GameHour,
        recentEvents
    });
});

app.MapGet("/api/world/entities", () =>
{
    var entities = new List<object>();
    foreach (var (id, state) in sim.World.Entities.All())
    {
        var profile = sim.GetProfile(id);
        entities.Add(new
        {
            id,
            displayName = profile?.DisplayName ?? id,
            embodied = state.Embodied,
            location = sim.World.Locations.GetLocation(id),
            isPossessed = slots.IsPossessed(id),
            tags = profile?.Identity?.Tags
        });
    }
    return Results.Ok(entities);
});

// ===================== ENTITY STATE =====================

app.MapGet("/api/entity/{id}/state", (string id) =>
{
    var state = sim.World.Entities.Get(id);
    if (state == null) return Results.NotFound(new { error = $"Entity '{id}' not found" });

    var profile = sim.GetProfile(id);
    var location = sim.World.Locations.GetLocation(id);
    var modifiers = sim.World.Modifiers.GetModifiers(id);
    var relationships = sim.World.Relationships.GetBySource(id)
        .Concat(sim.World.Relationships.GetByTarget(id))
        .Select(r => new
        {
            source = r.Source,
            target = r.Target,
            tags = r.Tags.ToList(),
            properties = r.Properties.ToDictionary(p => p.Key, p => p.Value.Value)
        });

    return Results.Ok(new
    {
        id,
        displayName = profile?.DisplayName ?? id,
        embodied = state.Embodied,
        location,
        isPossessed = slots.IsPossessed(id),
        busyUntilTick = state.BusyUntilTick,
        properties = state.Properties.ToDictionary(
            p => p.Key,
            p => new { p.Value.Value, p.Value.Min, p.Value.Max, p.Value.DecayRate, p.Value.Critical }),
        personality = state.Personality,
        identity = profile?.Identity,
        homeLocation = state.HomeLocation,
        modifiers = modifiers.Select(m => new
        {
            m.Id, m.Source, m.Type, m.Duration, m.Intensity, m.Priority, m.Flavor,
            actionBonus = m.ActionBonus,
            gossip = m.Gossip
        }),
        relationships
    });
});

app.MapGet("/api/entity/{id}/actions", (string id) =>
{
    var state = sim.World.Entities.Get(id);
    if (state == null) return Results.NotFound(new { error = $"Entity '{id}' not found" });

    var profile = sim.GetProfile(id);
    if (profile == null) return Results.NotFound(new { error = $"Profile for '{id}' not found" });

    var scored = UtilityScorer.ScoreAllCandidates(profile, state, sim.Actions, sim.World);

    var actions = scored.Select(s => new
    {
        actionId = s.Action.Id,
        displayName = s.Action.DisplayName,
        category = s.Action.Category,
        score = Math.Round(s.Score, 4),
        flavor = s.Action.Flavor?.OnStart?.FirstOrDefault(),
        steps = s.Action.Steps.Select(step => new
        {
            type = step.Type,
            entity = step.Entity,
            property = step.Property,
            amount = step.Amount,
            target = step.Target
        }).Where(step => step.type == "modifyProperty" || step.type == "moveTo")
    }).ToList();

    return Results.Ok(new
    {
        entityId = id,
        tick = sim.World.Clock.Tick,
        location = sim.World.Locations.GetLocation(id),
        actionCount = actions.Count,
        actions
    });
});

app.MapPost("/api/entity/{id}/act", (string id, ActRequest req, HttpContext ctx) =>
{
    // Auth check
    var authHeader = ctx.Request.Headers.Authorization.FirstOrDefault();
    if (!slots.IsTokenValidForEntity(authHeader, id))
        return Results.Unauthorized();

    if (!sim.ExternalActions.IsExternalEntity(id))
        return Results.BadRequest(new { error = $"Entity '{id}' is not externally controlled" });

    // Find the action definition
    var action = sim.Actions.FirstOrDefault(a => a.Id == req.ActionId);
    if (action == null)
        return Results.BadRequest(new { error = $"Action '{req.ActionId}' not found" });

    // Validate requirements
    var state = sim.World.Entities.Get(id);
    var profile = sim.GetProfile(id);
    if (state == null || profile == null)
        return Results.NotFound(new { error = $"Entity '{id}' not found" });

    if (!UtilityScorer.MeetsRequirements(profile, state, action, sim.World))
        return Results.BadRequest(new { error = $"Action '{req.ActionId}' requirements not met" });

    if (!UtilityScorer.IsAccessAllowed(profile, action))
        return Results.BadRequest(new { error = $"Action '{req.ActionId}' not allowed for entity" });

    // Enqueue for next evaluation
    sim.ExternalActions.Enqueue(id, action);
    return Results.Accepted(value: new
    {
        status = "queued",
        entityId = id,
        actionId = req.ActionId,
        currentTick = sim.World.Clock.Tick,
        busyUntilTick = state.BusyUntilTick,
        note = state.BusyUntilTick > sim.World.Clock.Tick
            ? $"Entity busy until tick {state.BusyUntilTick} — action will execute after"
            : "Action will execute on next tick"
    });
});

// ===================== POSSESSION =====================

app.MapPost("/api/entity/possess", (PossessRequest req) =>
{
    var slot = slots.Possess(req.EntityId, sim);
    if (slot == null)
        return Results.BadRequest(new { error = $"Cannot possess '{req.EntityId}' — entity not found or not embodied" });

    return Results.Ok(new
    {
        entityId = slot.EntityId,
        token = slot.Token,
        location = sim.World.Locations.GetLocation(slot.EntityId)
    });
});

app.MapPost("/api/entity/release", (ReleaseRequest req) =>
{
    slots.Release(req.EntityId, sim);
    return Results.Ok(new { status = "released", entityId = req.EntityId });
});

app.MapGet("/api/entity/slots", () =>
{
    return Results.Ok(slots.AllSlots.Values.Select(s => new
    {
        s.EntityId,
        location = sim.World.Locations.GetLocation(s.EntityId)
    }));
});

// ===================== WEBSOCKET =====================

app.Map("/ws/stream", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    var client = new ConnectedClient(ws);
    hub.AddClient(client);

    try
    {
        // Send initial state
        var initMsg = JsonSerializer.Serialize(new
        {
            type = "connected",
            tick = sim.World.Clock.Tick,
            gameTime = sim.World.Clock.FormatGameTime()
        }, jsonOpts);
        await client.SendAsync(initMsg);

        // Receive loop — handle client messages (token binding)
        var buffer = new byte[4096];
        while (ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
                break;

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                try
                {
                    var msg = JsonSerializer.Deserialize<WsClientMessage>(text, jsonOpts);
                    if (msg?.Token != null)
                    {
                        var entityId = slots.ValidateToken($"Bearer {msg.Token}");
                        if (entityId != null)
                        {
                            client.EntityId = entityId;
                            await client.SendAsync(JsonSerializer.Serialize(new
                            {
                                type = "bound",
                                entityId
                            }, jsonOpts));
                        }
                    }
                }
                catch { }
            }
        }
    }
    finally
    {
        hub.RemoveClient(client);
        if (ws.State == WebSocketState.Open)
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
    }
});

Console.WriteLine($"\nListening on http://localhost:3801");
app.Run("http://0.0.0.0:3801");

// ===================== HELPERS =====================

object SerializeTickResult(TickResult result) => new
{
    tick = result.Tick,
    gameTime = result.GameTime,
    events = result.Events.Select(e => new
    {
        e.AutonomeId,
        e.ActionId,
        score = Math.Round(e.Score, 4),
        e.Location,
        topCandidates = e.TopCandidates.Select(c => new { c.ActionId, score = Math.Round(c.Score, 4) }),
        properties = e.PropertySnapshot
    })
};

// ===================== REQUEST/RESPONSE TYPES =====================

record AutoAdvanceRequest(float TicksPerSecond = 1f);
record ActRequest(string ActionId);
record PossessRequest(string EntityId);
record ReleaseRequest(string EntityId);
record WsClientMessage(string? Token = null, string? Action = null);
