using Autonome.Core.Model;

namespace Autonome.Core.Simulation;

/// <summary>
/// Tracks externally-controlled entities and their pending actions.
/// External entities skip UtilityScorer — their actions come from API input.
/// </summary>
public class ExternalActionQueue
{
    private readonly HashSet<string> _externalEntityIds = new();
    private readonly Dictionary<string, ActionDefinition> _pending = new();

    public void RegisterExternal(string entityId)
        => _externalEntityIds.Add(entityId);

    public void UnregisterExternal(string entityId)
    {
        _externalEntityIds.Remove(entityId);
        _pending.Remove(entityId);
    }

    public bool IsExternalEntity(string entityId)
        => _externalEntityIds.Contains(entityId);

    public void Enqueue(string entityId, ActionDefinition action)
        => _pending[entityId] = action;

    public bool TryDequeue(string entityId, out ActionDefinition? action)
        => _pending.Remove(entityId, out action);

    public bool HasPending(string entityId)
        => _pending.ContainsKey(entityId);

    public IReadOnlySet<string> ExternalEntityIds => _externalEntityIds;
}
