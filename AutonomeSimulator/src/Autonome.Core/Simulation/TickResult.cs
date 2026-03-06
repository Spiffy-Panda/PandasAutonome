namespace Autonome.Core.Simulation;

/// <summary>
/// Result of a single tick advancement. Contains all action events that occurred.
/// </summary>
public sealed class TickResult
{
    public int Tick { get; }
    public string GameTime { get; }
    public List<ActionEvent> Events { get; } = [];

    public TickResult(int tick, string gameTime)
    {
        Tick = tick;
        GameTime = gameTime;
    }
}
