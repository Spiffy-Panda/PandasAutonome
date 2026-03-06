using Autonome.Core.Simulation;

namespace Autonome.Web;

/// <summary>
/// Manages external autonome slots — tracks which entities are externally controlled
/// and their auth tokens.
/// </summary>
public class ExternalSlotManager
{
    private readonly Dictionary<string, ExternalSlot> _slots = new();
    private readonly Dictionary<string, string> _tokenToEntity = new();

    public ExternalSlot? Possess(string entityId, InteractiveSimulation sim)
    {
        var state = sim.World.Entities.Get(entityId);
        if (state == null) return null;
        if (!state.Embodied) return null;

        // Already possessed — return existing slot
        if (_slots.TryGetValue(entityId, out var existing))
            return existing;

        string token = $"ext_{Guid.NewGuid():N}";
        var slot = new ExternalSlot(entityId, token);
        _slots[entityId] = slot;
        _tokenToEntity[token] = entityId;

        sim.ExternalActions.RegisterExternal(entityId);

        return slot;
    }

    public void Release(string entityId, InteractiveSimulation sim)
    {
        if (_slots.Remove(entityId, out var slot))
        {
            _tokenToEntity.Remove(slot.Token);
            sim.ExternalActions.UnregisterExternal(entityId);
        }
    }

    public string? ValidateToken(string? authHeader)
    {
        if (string.IsNullOrEmpty(authHeader)) return null;

        // Extract bearer token
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            authHeader = authHeader[7..];

        return _tokenToEntity.TryGetValue(authHeader, out var entityId) ? entityId : null;
    }

    public bool IsTokenValidForEntity(string? authHeader, string entityId)
    {
        var tokenEntity = ValidateToken(authHeader);
        return tokenEntity == entityId;
    }

    public bool IsPossessed(string entityId) => _slots.ContainsKey(entityId);

    public IReadOnlyDictionary<string, ExternalSlot> AllSlots => _slots;
}

public sealed record ExternalSlot(string EntityId, string Token);
