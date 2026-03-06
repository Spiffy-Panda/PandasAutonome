using Autonome.Core.Model;
using Autonome.Core.Simulation;
using Autonome.Core.World;

namespace Autonome.Data;

/// <summary>
/// Assembles a fully initialized WorldState from raw DataLoader output.
/// Shared by Autonome.Web (InteractiveSimulation) and Godot (SimulationBridge).
/// </summary>
public static class WorldBuilder
{
    public static WorldBuildResult Build(LoadResult loadResult)
    {
        var warnings = new List<string>();
        var world = new WorldState();

        // 1. Register entities
        foreach (var profile in loadResult.Profiles)
        {
            var resolvedLevels = DataLoader.ResolvePropertyLevels(profile, loadResult.PropertyLevels);
            world.Entities.Register(profile, resolvedLevels);
        }

        // 2. Register locations
        foreach (var loc in loadResult.Locations)
        {
            world.Locations.AddLocation(loc);
            if (loc.Properties != null)
                world.LocationStates.Initialize(loc.Id, loc.Properties);
        }

        // 3. Register relationships
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

        // 4. Build authority graph
        world.AuthorityGraph.Build(world.Relationships);
        try
        {
            world.AuthorityGraph.ValidateAcyclic();
        }
        catch (Exception ex)
        {
            warnings.Add($"Authority graph cycle: {ex.Message}");
        }

        // 5. Build routing table
        world.Locations.BuildRoutingTable();

        // 6. Spawn embodied entities — prefer homeLocation, fallback to org-linked
        var profileLookup = loadResult.Profiles.ToDictionary(p => p.Id);
        int spawnCount = 0;
        foreach (var profile in loadResult.Profiles)
        {
            if (!profile.Embodied) continue;

            string? spawnLocation = null;

            // Priority 1: NPC's own homeLocation
            if (profile.HomeLocation != null)
            {
                spawnLocation = profile.HomeLocation;
            }
            else
            {
                // Priority 2: Org-linked location
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
            }

            spawnLocation ??= world.Locations.AllLocationIds.FirstOrDefault();
            if (spawnLocation != null)
            {
                world.Locations.SetLocation(profile.Id, spawnLocation);
                spawnCount++;
            }
        }

        // 7. Load initial modifiers
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

        var config = new SimulationConfig(
            TotalTicks: int.MaxValue,
            SnapshotInterval: 0,
            Events: loadResult.Events
        );

        return new WorldBuildResult(
            World: world,
            Profiles: loadResult.Profiles,
            Actions: loadResult.Actions,
            Config: config,
            SpawnCount: spawnCount,
            Warnings: warnings
        );
    }
}

public sealed record WorldBuildResult(
    WorldState World,
    IReadOnlyList<AutonomeProfile> Profiles,
    IReadOnlyList<ActionDefinition> Actions,
    SimulationConfig Config,
    int SpawnCount,
    List<string> Warnings
);
