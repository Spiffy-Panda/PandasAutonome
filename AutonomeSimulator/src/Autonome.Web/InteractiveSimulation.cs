using Autonome.Core.Model;
using Autonome.Core.Simulation;
using Autonome.Core.World;
using Autonome.Data;

namespace Autonome.Web;

/// <summary>
/// Manages an interactive simulation instance with tick-by-tick control.
/// Thread-safe: all state mutations go through a lock.
/// </summary>
public class InteractiveSimulation
{
    public WorldState World { get; private set; } = null!;
    public IReadOnlyList<AutonomeProfile> Profiles { get; private set; } = [];
    public IReadOnlyList<ActionDefinition> Actions { get; private set; } = [];
    public SimulationConfig Config { get; private set; } = null!;
    public ExternalActionQueue ExternalActions { get; } = new();

    /// <summary>Rolling event log — last N events for API queries.</summary>
    public List<ActionEvent> RecentEvents { get; } = [];
    private const int MaxRecentEvents = 500;

    private readonly SimulationRunner _runner = new();
    private readonly object _lock = new();

    // Auto-advance state
    public bool IsAutoAdvancing { get; private set; }
    public float TicksPerSecond { get; set; } = 1f;
    private CancellationTokenSource? _autoAdvanceCts;
    private Task? _autoAdvanceTask;

    /// <summary>Fired after each tick resolves. Used by WebSocketHub.</summary>
    public event Action<TickResult>? OnTickCompleted;

    public bool IsLoaded => World != null;
    public string DataPath { get; private set; } = "";

    public void Load(string dataPath)
    {
        DataPath = dataPath;
        var loader = new DataLoader();
        var loadResult = loader.Load(dataPath);

        if (loadResult.HasErrors)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            foreach (var err in loadResult.Errors)
                Console.WriteLine($"  Load error: {err}");
            Console.ResetColor();
        }

        Console.WriteLine($"Loaded: {loadResult.Profiles.Count} profiles, {loadResult.Actions.Count} actions, " +
                          $"{loadResult.Relationships.Count} relationships, {loadResult.Locations.Count} locations");

        // Build world state (mirrors Program.cs logic)
        var world = new WorldState();

        foreach (var profile in loadResult.Profiles)
        {
            var resolvedLevels = DataLoader.ResolvePropertyLevels(profile, loadResult.PropertyLevels);
            world.Entities.Register(profile, resolvedLevels);
        }

        foreach (var loc in loadResult.Locations)
        {
            world.Locations.AddLocation(loc);
            if (loc.Properties != null)
                world.LocationStates.Initialize(loc.Id, loc.Properties);
        }

        foreach (var relData in loadResult.Relationships)
        {
            var rel = new Relationship
            {
                Source = relData.Source,
                Target = relData.Target,
                Tags = new HashSet<string>(relData.Tags),
                Properties = relData.Properties?.ToDictionary(
                    p => p.Key,
                    p => new PropertyState(new PropertyDefinition(p.Key, p.Value.Value, p.Value.Min, p.Value.Max, p.Value.DecayRate))
                ) ?? new Dictionary<string, PropertyState>()
            };
            world.Relationships.Add(rel);
        }

        // Build authority graph
        world.AuthorityGraph.Build(world.Relationships);
        try
        {
            world.AuthorityGraph.ValidateAcyclic();
            Console.WriteLine("Authority graph: valid (acyclic)");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Authority graph error: {ex.Message}");
            Console.ResetColor();
        }

        // Build routing table
        world.Locations.BuildRoutingTable();
        Console.WriteLine($"Routing table: {world.Locations.LocationCount} locations, {world.Locations.EdgeCount} edges");

        // Spawn embodied entities
        var profileLookup = loadResult.Profiles.ToDictionary(p => p.Id);
        int spawnCount = 0;
        foreach (var profile in loadResult.Profiles)
        {
            if (!profile.Embodied) continue;

            string? spawnLocation = null;
            var superiors = world.AuthorityGraph.GetSuperiors(profile.Id);

            string? guildSuperior = null;
            string? townSuperior = null;

            foreach (var supId in superiors)
            {
                if (!profileLookup.TryGetValue(supId, out var supProfile)) continue;
                var tags = supProfile.Identity?.Tags;
                if (tags == null) continue;

                if (tags.Contains("guild") && guildSuperior == null)
                    guildSuperior = supId;
                else if (tags.Contains("settlement") && townSuperior == null)
                    townSuperior = supId;
            }

            var orgId = guildSuperior ?? townSuperior;
            if (orgId != null && profileLookup.TryGetValue(orgId, out var orgProfile))
            {
                if (orgProfile.HomeLocation != null)
                    spawnLocation = orgProfile.HomeLocation;
                else
                {
                    var orgTags = orgProfile.Identity?.Tags;
                    if (orgTags != null)
                    {
                        foreach (var tag in orgTags)
                        {
                            foreach (var locId in world.Locations.AllLocationIds)
                            {
                                var locDef = world.Locations.GetDefinition(locId);
                                if (locDef != null && locDef.Tags.Contains(tag))
                                {
                                    spawnLocation = locId;
                                    break;
                                }
                            }
                            if (spawnLocation != null) break;
                        }
                    }
                }
            }

            spawnLocation ??= world.Locations.AllLocationIds.FirstOrDefault();
            if (spawnLocation != null)
            {
                world.Locations.SetLocation(profile.Id, spawnLocation);
                spawnCount++;
            }
        }
        Console.WriteLine($"Spawned {spawnCount} entities at org locations");

        // Load initial modifiers
        foreach (var profile in loadResult.Profiles)
        {
            if (profile.InitialModifiers == null) continue;
            int i = 0;
            foreach (var init in profile.InitialModifiers)
            {
                var mod = new Modifier
                {
                    Id = $"init_{profile.Id}_{i++}",
                    Source = profile.Id,
                    Type = init.Type,
                    Target = profile.Id,
                    ActionBonus = init.ActionBonus,
                    PropertyMod = init.PropertyMod,
                    DecayRate = init.DecayRate,
                    Intensity = init.Intensity,
                    Duration = init.Duration,
                    Flavor = init.Flavor
                };
                world.Modifiers.Add(mod);
            }
        }

        World = world;
        Profiles = loadResult.Profiles;
        Actions = loadResult.Actions;
        Config = new SimulationConfig(
            TotalTicks: int.MaxValue, // No limit in interactive mode
            SnapshotInterval: 0,     // No periodic snapshots
            Events: loadResult.Events
        );

        Console.WriteLine("Interactive simulation ready.");
    }

    public TickResult AdvanceTick()
    {
        lock (_lock)
        {
            var result = _runner.TickOnce(World, Profiles, Actions, Config, ExternalActions);

            // Append to rolling log
            RecentEvents.AddRange(result.Events);
            while (RecentEvents.Count > MaxRecentEvents)
                RecentEvents.RemoveAt(0);

            OnTickCompleted?.Invoke(result);
            return result;
        }
    }

    public void StartAutoAdvance(float ticksPerSecond)
    {
        StopAutoAdvance();
        TicksPerSecond = ticksPerSecond;
        IsAutoAdvancing = true;
        _autoAdvanceCts = new CancellationTokenSource();
        var ct = _autoAdvanceCts.Token;

        _autoAdvanceTask = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                AdvanceTick();
                int delayMs = (int)(1000f / TicksPerSecond);
                await Task.Delay(Math.Max(delayMs, 10), ct);
            }
        }, ct);
    }

    public void StopAutoAdvance()
    {
        IsAutoAdvancing = false;
        _autoAdvanceCts?.Cancel();
        try { _autoAdvanceTask?.Wait(); } catch { }
        _autoAdvanceCts?.Dispose();
        _autoAdvanceCts = null;
        _autoAdvanceTask = null;
    }

    /// <summary>Get a profile by ID from the loaded profiles list.</summary>
    public AutonomeProfile? GetProfile(string entityId)
        => Profiles.FirstOrDefault(p => p.Id == entityId);
}
