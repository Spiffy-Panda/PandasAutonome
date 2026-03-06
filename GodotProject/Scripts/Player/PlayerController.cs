using Godot;
using AutonomeSim.Core;

namespace AutonomeSim.Player;

/// <summary>
/// Manages player possession of an NPC and action selection.
/// </summary>
public partial class PlayerController : Node
{
    private SimulationBridge _bridge = null!;

    [Signal] public delegate void PossessionChangedEventHandler(string entityId, bool possessed);
    [Signal] public delegate void ActionQueuedEventHandler(string actionId);

    public string? PossessedEntityId => _bridge?.PossessedEntityId;
    public bool HasPossession => PossessedEntityId != null;

    public override void _Ready()
    {
        _bridge = GetNode<SimulationBridge>("/root/Main/SimulationBridge");
    }

    public void Possess(string entityId)
    {
        _bridge.PossessEntity(entityId);
        EmitSignal(SignalName.PossessionChanged, entityId, true);
    }

    public void Release()
    {
        var id = PossessedEntityId;
        _bridge.ReleaseEntity();
        if (id != null)
            EmitSignal(SignalName.PossessionChanged, id, false);
    }

    public void PickAction(string actionId)
    {
        _bridge.EnqueueAction(actionId);
        EmitSignal(SignalName.ActionQueued, actionId);
    }
}
