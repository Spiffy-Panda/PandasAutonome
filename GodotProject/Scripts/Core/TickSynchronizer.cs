namespace AutonomeSim.Core;

public enum TickMode { Paused, ManualStep, AutoAdvance }

/// <summary>
/// Manages tick timing relative to Godot's frame delta.
/// Returns how many simulation ticks should execute this frame.
/// </summary>
public class TickSynchronizer
{
    public TickMode Mode { get; set; } = TickMode.Paused;
    public float TicksPerSecond { get; set; } = 1f;
    public bool PendingManualTick { get; set; }

    private double _accumulator;

    /// <summary>
    /// Call from _Process(delta). Returns number of ticks to execute this frame.
    /// </summary>
    public int Update(double delta)
    {
        switch (Mode)
        {
            case TickMode.Paused:
                return 0;

            case TickMode.ManualStep:
                if (PendingManualTick)
                {
                    PendingManualTick = false;
                    return 1;
                }
                return 0;

            case TickMode.AutoAdvance:
                _accumulator += delta;
                double tickInterval = 1.0 / TicksPerSecond;
                int ticks = 0;
                while (_accumulator >= tickInterval)
                {
                    _accumulator -= tickInterval;
                    ticks++;
                }
                // Cap to prevent spiral-of-death
                return Math.Min(ticks, 5);

            default:
                return 0;
        }
    }

    public void Reset()
    {
        _accumulator = 0;
        PendingManualTick = false;
    }
}
