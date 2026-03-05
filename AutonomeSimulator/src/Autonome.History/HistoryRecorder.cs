using Autonome.Core.Model;
using Autonome.Core.Runtime;
using Autonome.Core.World;

namespace Autonome.History;

/// <summary>
/// Records simulation events into a structured history log.
/// </summary>
public class HistoryRecorder
{
    private readonly List<HistoryEntry> _entries = [];

    public IReadOnlyList<HistoryEntry> Entries => _entries;

    public void RecordActionChosen(
        AutonomeProfile profile,
        EntityState state,
        ScoredAction chosen,
        List<ScoredAction> candidates,
        WorldState world)
    {
        _entries.Add(new HistoryEntry
        {
            Tick = world.Clock.Tick,
            GameTime = world.Clock.FormatGameTime(),
            Type = "action_chosen",
            AutonomeId = profile.Id,
            Embodied = profile.Embodied,
            ActionId = chosen.Action.Id,
            Score = chosen.Score,
            TopCandidates = candidates.Take(5)
                .Select(c => new CandidateEntry(c.Action.Id, c.Score))
                .ToList(),
            PropertySnapshot = state.Properties
                .ToDictionary(p => p.Key, p => p.Value.Value)
        });
    }

    public void RecordModifierEmitted(
        string sourceId,
        ModifierTemplate template,
        int targetCount,
        WorldState world)
    {
        _entries.Add(new HistoryEntry
        {
            Tick = world.Clock.Tick,
            GameTime = world.Clock.FormatGameTime(),
            Type = "modifier_emitted",
            AutonomeId = sourceId,
            Message = $"Emitted {template.Type} '{template.Id}' to {targetCount} targets"
        });
    }

    public void RecordPropertyChanged(
        string entityId,
        string propertyId,
        float oldValue,
        float newValue,
        string cause,
        string? actorId,
        WorldState world)
    {
        _entries.Add(new HistoryEntry
        {
            Tick = world.Clock.Tick,
            GameTime = world.Clock.FormatGameTime(),
            Type = "property_changed",
            AutonomeId = entityId,
            Message = $"{propertyId}: {oldValue:F3} -> {newValue:F3} ({cause})"
        });
    }

    public void Clear() => _entries.Clear();
}
