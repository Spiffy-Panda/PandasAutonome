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

        var buildResult = WorldBuilder.Build(loadResult);

        foreach (var warning in buildResult.Warnings)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  Warning: {warning}");
            Console.ResetColor();
        }

        Console.WriteLine($"Routing table: {buildResult.World.Locations.LocationCount} locations, " +
                          $"{buildResult.World.Locations.EdgeCount} edges");
        Console.WriteLine($"Spawned {buildResult.SpawnCount} entities at org locations");

        World = buildResult.World;
        Profiles = buildResult.Profiles;
        Actions = buildResult.Actions;
        Config = buildResult.Config;

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
