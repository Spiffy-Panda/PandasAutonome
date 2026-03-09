using Godot;
using Autonome.Core.Model;
using Autonome.Core.Runtime;
using Autonome.Core.Simulation;
using Autonome.Core.World;
using Autonome.Data;

namespace AutonomeSim.Core;

/// <summary>
/// Central simulation orchestrator. Wraps Autonome.Core's SimulationRunner
/// and drives tick timing from Godot's _Process loop.
/// Add as autoload or child of Main scene.
/// </summary>
public partial class SimulationBridge : Node
{
	[Export] public string DataPath { get; set; } = "../AutonomeSimulator/worlds/coastal_city";

	// Simulation state
	public WorldState World { get; private set; } = null!;
	public IReadOnlyList<AutonomeProfile> Profiles { get; private set; } = [];
	public IReadOnlyList<ActionDefinition> Actions { get; private set; } = [];

	private SimulationConfig _config = null!;
	private readonly SimulationRunner _runner = new();
	private readonly ExternalActionQueue _externalActions = new();
	private readonly TickSynchronizer _tickSync = new();

	// Event accumulation for analysis export
	private SimulationResult _simulationResult = new();
	private LoadResult? _loadResult;

	// Location tracking for move detection
	private readonly Dictionary<string, string?> _previousLocations = new();

	// Profile/action lookup caches
	private Dictionary<string, AutonomeProfile> _profileLookup = new();
	private Dictionary<string, ActionDefinition> _actionLookup = new();

	// Possession state
	public string? PossessedEntityId { get; private set; }

	public bool IsLoaded { get; private set; }
	public string ResolvedDataPath { get; private set; } = "";
	public int CurrentTick => World?.Clock.Tick ?? 0;
	public string CurrentGameTime => World?.Clock.FormatGameTime() ?? "";
	public TickMode TickMode => _tickSync.Mode;
	public float TicksPerSecond => _tickSync.TicksPerSecond;

	// Signals
	[Signal] public delegate void SimulationLoadedEventHandler();
	[Signal] public delegate void TickCompletedEventHandler(int tick, string gameTime);
	[Signal] public delegate void EntityMovedEventHandler(string entityId, string fromLocation, string toLocation);
	[Signal] public delegate void EntityActionEventHandler(string entityId, string actionId, string location, float score);
	[Signal] public delegate void EntityPossessedEventHandler(string entityId);
	[Signal] public delegate void EntityReleasedEventHandler(string entityId);
	[Signal] public delegate void ShipArrivedEventHandler(string vesselName, string cargo, string locationId);

	public override void _Ready()
	{
		if (!string.IsNullOrEmpty(DataPath))
		{
			// Resolve relative paths from the Godot project directory
			var path = DataPath;
			if (!System.IO.Path.IsPathRooted(path))
				path = System.IO.Path.GetFullPath(System.IO.Path.Combine(
					ProjectSettings.GlobalizePath("res://"), path));

			ResolvedDataPath = path;
			LoadSimulation(path);
		}
	}

	public void LoadSimulation(string dataPath)
	{
		var loader = new DataLoader();
		var loadResult = loader.Load(dataPath);

		if (loadResult.HasErrors)
		{
			foreach (var err in loadResult.Errors)
				GD.PrintErr($"Load error: {err}");
		}

		var buildResult = WorldBuilder.Build(loadResult);

		foreach (var warning in buildResult.Warnings)
			GD.Print($"Warning: {warning}");

		World = buildResult.World;
		Profiles = buildResult.Profiles;
		Actions = buildResult.Actions;
		_config = buildResult.Config;

		_profileLookup = Profiles.ToDictionary(p => p.Id);
		_actionLookup = Actions.ToDictionary(a => a.Id);

		// Store load result for analysis export
		_loadResult = loadResult;
		_simulationResult = new SimulationResult
		{
			MinutesPerTick = World.Clock.MinutesPerTick
		};

		// Snapshot initial locations
		foreach (var profile in Profiles)
		{
			if (!profile.Embodied) continue;
			_previousLocations[profile.Id] = World.Locations.GetLocation(profile.Id);
		}

		IsLoaded = true;
		GD.Print($"Simulation loaded: {Profiles.Count} entities, {World.Locations.LocationCount} locations, {Actions.Count} actions");
		GD.Print($"Spawned {buildResult.SpawnCount} embodied entities");

		EmitSignal(SignalName.SimulationLoaded);
	}

	public override void _Process(double delta)
	{
		if (!IsLoaded) return;

		int tickCount = _tickSync.Update(delta);
		for (int i = 0; i < tickCount; i++)
			ExecuteTick();
	}

	private void ExecuteTick()
	{
		var result = _runner.TickOnce(World, Profiles, Actions, _config, _externalActions);

		// Accumulate events for analysis export
		_simulationResult.ActionEvents.AddRange(result.Events);

		// Periodic snapshot (every 100 ticks, same as CLI default)
		if (result.Tick % 100 == 0)
		{
			_simulationResult.Snapshots.Add(
				SimulationRunner.TakeSnapshot(World, Profiles));
		}

		// Detect moves and emit signals
		foreach (var evt in result.Events)
		{
			EmitSignal(SignalName.EntityAction, evt.AutonomeId, evt.ActionId, evt.Location ?? "", evt.Score);

			if (_previousLocations.TryGetValue(evt.AutonomeId, out var prevLoc))
			{
				var newLoc = World.Locations.GetLocation(evt.AutonomeId);
				if (newLoc != prevLoc && prevLoc != null && newLoc != null)
				{
					EmitSignal(SignalName.EntityMoved, evt.AutonomeId, prevLoc, newLoc);
				}
				_previousLocations[evt.AutonomeId] = newLoc;
			}
		}

		// Detect ship arrivals from external events
		DetectShipArrivals(result.Tick);

		EmitSignal(SignalName.TickCompleted, result.Tick, result.GameTime);
	}

	/// <summary>
	/// Check if any external events fired this tick and emit ShipArrived signals.
	/// Groups events by ship (same triggerTick + repeatInterval).
	/// </summary>
	private void DetectShipArrivals(int tick)
	{
		if (_config.Events == null || _config.Events.Count == 0) return;

		// Group fired events by (triggerTick, repeatInterval) = same vessel
		var shipCargo = new Dictionary<(int trigger, int repeat), List<(string prop, float amount, string location)>>();

		foreach (var evt in _config.Events)
		{
			if (evt.Type != "modify_location_property") continue;
			if (evt.Location == null || evt.Property == null || evt.Amount == null) continue;

			bool fires = tick == evt.TriggerTick ||
				(evt.RepeatInterval.HasValue && evt.RepeatInterval.Value > 0 &&
				 tick > evt.TriggerTick &&
				 (tick - evt.TriggerTick) % evt.RepeatInterval.Value == 0);

			if (!fires) continue;

			var key = (evt.TriggerTick, evt.RepeatInterval ?? 0);
			if (!shipCargo.ContainsKey(key))
				shipCargo[key] = new();
			shipCargo[key].Add((evt.Property, evt.Amount.Value, evt.Location));
		}

		// Emit signal for each vessel
		foreach (var (key, cargo) in shipCargo)
		{
			// Name ships by their schedule
			string vesselName = key.repeat switch
			{
				200 => "Merchant vessel",
				350 => "Supply barge",
				_ => "Ship"
			};

			// Build cargo manifest — only show positive amounts (deliveries)
			var parts = new List<string>();
			string? location = null;
			foreach (var (prop, amount, loc) in cargo)
			{
				location ??= loc;
				string label = prop.Replace("_supply", "").Replace("_", " ");
				if (amount > 0)
					parts.Add($"+{amount:F0} {label}");
				else
					parts.Add($"{amount:F0} {label}");
			}

			if (parts.Count > 0 && location != null)
			{
				var manifest = string.Join(", ", parts);
				EmitSignal(SignalName.ShipArrived, vesselName, manifest, location);
			}
		}
	}

	// --- Tick control ---

	public void StepOneTick()
	{
		_tickSync.Mode = TickMode.ManualStep;
		_tickSync.PendingManualTick = true;
	}

	public void StartAutoAdvance(float tps)
	{
		_tickSync.TicksPerSecond = tps;
		_tickSync.Mode = TickMode.AutoAdvance;
	}

	public void PauseSimulation()
	{
		_tickSync.Mode = TickMode.Paused;
		_tickSync.Reset();
	}

	public void SetSpeed(float tps)
	{
		_tickSync.TicksPerSecond = Mathf.Clamp(tps, 0.5f, 20f);
	}

	// --- Possession ---

	public void PossessEntity(string entityId)
	{
		if (PossessedEntityId != null)
			ReleaseEntity();

		_externalActions.RegisterExternal(entityId);
		PossessedEntityId = entityId;
		EmitSignal(SignalName.EntityPossessed, entityId);
	}

	public void ReleaseEntity()
	{
		if (PossessedEntityId == null) return;
		var id = PossessedEntityId;
		_externalActions.UnregisterExternal(id);
		PossessedEntityId = null;
		EmitSignal(SignalName.EntityReleased, id);
	}

	public void EnqueueAction(string actionId)
	{
		if (PossessedEntityId == null) return;
		if (!_actionLookup.TryGetValue(actionId, out var action)) return;
		_externalActions.Enqueue(PossessedEntityId, action);
	}

	// --- Queries ---

	public AutonomeProfile? GetProfile(string entityId)
		=> _profileLookup.GetValueOrDefault(entityId);

	public ActionDefinition? GetAction(string actionId)
		=> _actionLookup.GetValueOrDefault(actionId);

	public EntityState? GetEntityState(string entityId)
		=> World?.Entities.Get(entityId);

	public string? GetEntityLocation(string entityId)
		=> World?.Locations.GetLocation(entityId);

	public List<ScoredAction> GetAvailableActions(string entityId)
	{
		if (World == null) return [];
		var profile = GetProfile(entityId);
		var state = GetEntityState(entityId);
		if (profile == null || state == null) return [];

		return UtilityScorer.ScoreAllCandidates(profile, state, Actions, World);
	}

	public IEnumerable<string> GetEntitiesAtLocation(string locationId)
		=> World?.Locations.GetEntitiesAtLocation(locationId) ?? [];

	// --- Analysis export ---

	public SimulationResult GetSimulationResult() => _simulationResult;

	public LoadResult? GetLoadResult() => _loadResult;
}
