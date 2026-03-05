using Autonome.Analysis;
using Autonome.Core.Model;
using Autonome.Core.Simulation;
using Autonome.Core.World;
using Autonome.Data;
using Autonome.History;

// Parse CLI arguments
string dataPath = GetArg(args, "--data", "data");
int ticks = int.Parse(GetArg(args, "--ticks", "1000"));
string output = GetArg(args, "--output", "output/simulation_result.json");
bool validateOnly = args.Contains("--validate-only");
bool analyze = args.Contains("--analyze");
int snapshotInterval = int.Parse(GetArg(args, "--snapshot-interval", "100"));

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("Autonome Simulator - Hierarchical Utility AI");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --data <path>              Data directory (default: data)");
    Console.WriteLine("  --ticks <n>                Simulation ticks (default: 1000)");
    Console.WriteLine("  --output <path>            Output JSON file (default: output/simulation_result.json)");
    Console.WriteLine("  --validate-only            Only validate data, don't run simulation");
    Console.WriteLine("  --snapshot-interval <n>    Ticks between snapshots (default: 100)");
    Console.WriteLine("  --analyze                  Generate analysis report after simulation");
    return 0;
}

Console.WriteLine("=== Autonome Simulator v3 ===");
Console.WriteLine($"Data path: {dataPath}");

// Load data
var loader = new DataLoader();
var loadResult = loader.Load(dataPath);

if (loadResult.HasErrors)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"\n{loadResult.Errors.Count} load error(s):");
    foreach (var error in loadResult.Errors)
        Console.WriteLine($"  - {error}");
    Console.ResetColor();
}

Console.WriteLine($"\nLoaded: {loadResult.Profiles.Count} profiles, {loadResult.Actions.Count} actions, " +
                  $"{loadResult.Relationships.Count} relationships, {loadResult.Locations.Count} locations" +
                  (loadResult.PropertyLevels != null ? $", {loadResult.PropertyLevels.Sets.Count} property level sets" : "") +
                  (loadResult.Events != null ? $", {loadResult.Events.Count} events" : ""));

// Validate
var validator = new SchemaValidator();
var validationErrors = validator.Validate(loadResult);
if (validationErrors.Count > 0)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"\n{validationErrors.Count} validation warning(s):");
    foreach (var error in validationErrors)
        Console.WriteLine($"  - {error}");
    Console.ResetColor();
}

if (validateOnly)
{
    Console.WriteLine("\nValidation complete.");
    return 0;
}

if (loadResult.Profiles.Count == 0)
{
    Console.WriteLine("\nNo profiles loaded - nothing to simulate.");
    return 0;
}

// Build world state
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
    return 1;
}

// Build routing table for location travel costs
world.Locations.BuildRoutingTable();
Console.WriteLine($"Routing table: {world.Locations.LocationCount} locations, {world.Locations.EdgeCount} edges");

// Spawn embodied entities at org locations (guild-first priority)
var profileLookup = loadResult.Profiles.ToDictionary(p => p.Id);
int spawnCount = 0;
foreach (var profile in loadResult.Profiles)
{
    if (!profile.Embodied) continue;

    string? spawnLocation = null;

    // Get superiors from authority graph
    var superiors = world.AuthorityGraph.GetSuperiors(profile.Id);

    // Guild-first: check guild superiors before town superiors
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

    // Try guild first, then town
    var orgId = guildSuperior ?? townSuperior;
    if (orgId != null && profileLookup.TryGetValue(orgId, out var orgProfile))
    {
        // Use homeLocation if set
        if (orgProfile.HomeLocation != null)
        {
            spawnLocation = orgProfile.HomeLocation;
        }
        else
        {
            // Fallback: find location matching org's tags
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

    // Final fallback: first location
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

// Run simulation
Console.WriteLine($"\nRunning simulation for {ticks} ticks...");
var runner = new SimulationRunner();
var config = new SimulationConfig(
    ticks,
    snapshotInterval,
    (current, total) => Console.Write($"\r  Tick {current}/{total} ({100 * current / total}%)"),
    loadResult.Events
);

var result = runner.Run(world, loadResult.Profiles, loadResult.Actions, config);
Console.WriteLine($"\r  Completed {ticks} ticks.                    ");

// Export & Analyze
if (analyze)
{
    // When analyzing, create a timestamped run folder and put everything in it
    var analysisDir = Path.Combine(Path.GetDirectoryName(output)!, "analysis");
    var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
    var runDir = Path.Combine(analysisDir, stamp);
    Directory.CreateDirectory(runDir);

    // Write simulation result into the run folder
    var simOutputPath = Path.Combine(runDir, "simulation_result.json");
    HistoryExporter.Export(result, simOutputPath);
    Console.WriteLine($"\nResults written to: {simOutputPath}");
    Console.WriteLine($"  {result.ActionEvents.Count} action events");
    Console.WriteLine($"  {result.Snapshots.Count} snapshots");

    // Write analysis reports into the same folder
    Console.WriteLine("\nRunning analysis...");
    var analysisResult = SimulationAnalyzer.Analyze(result);
    ReportWriter.WriteToDir(analysisResult, runDir);

    // Inventory analysis (location stockpiles, sources, sinks)
    var inventoryResult = InventoryAnalyzer.Analyze(result, loadResult.Actions, loadResult.Locations, loadResult.Events);
    ReportWriter.WriteInventory(inventoryResult, runDir);

    Console.WriteLine($"Analysis written to: {runDir}/");
    Console.WriteLine($"  simulation_result.json - raw simulation data");
    Console.WriteLine($"  report.txt   - human-readable summary");
    Console.WriteLine($"  report.json  - machine-readable data");
    Console.WriteLine($"  inventory.json - location inventory analysis ({inventoryResult.Locations.Count} locations)");
    Console.WriteLine($"  entities/    - per-entity detail files ({analysisResult.Entities.Count} entities)");
}
else
{
    // Without analysis, write to the specified output path
    HistoryExporter.Export(result, output);
    Console.WriteLine($"\nResults written to: {output}");
    Console.WriteLine($"  {result.ActionEvents.Count} action events");
    Console.WriteLine($"  {result.Snapshots.Count} snapshots");
}

return 0;

static string GetArg(string[] args, string name, string defaultValue)
{
    int idx = Array.IndexOf(args, name);
    if (idx >= 0 && idx + 1 < args.Length)
        return args[idx + 1];
    return defaultValue;
}
