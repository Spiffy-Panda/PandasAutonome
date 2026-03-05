namespace Autonome.Core.Simulation;

/// <summary>
/// Tracks simulation time: discrete ticks and derived game-time values.
/// </summary>
public sealed class SimulationClock
{
    /// <summary>Current discrete tick count.</summary>
    public int Tick { get; private set; }

    /// <summary>Game-minutes elapsed per tick.</summary>
    public float MinutesPerTick { get; init; } = 1f;

    /// <summary>Total game-minutes elapsed.</summary>
    public float TotalMinutes => Tick * MinutesPerTick;

    /// <summary>Current game hour (0-23).</summary>
    public float GameHour => (TotalMinutes / 60f) % 24f;

    /// <summary>Current game day (1-based).</summary>
    public int GameDay => (int)(TotalMinutes / (60f * 24f)) + 1;

    public void Advance(int ticks = 1)
    {
        Tick += ticks;
    }

    public string FormatGameTime()
    {
        int hour = (int)GameHour;
        int minute = (int)(TotalMinutes % 60f);
        return $"Day {GameDay}, {hour:D2}:{minute:D2}";
    }
}
