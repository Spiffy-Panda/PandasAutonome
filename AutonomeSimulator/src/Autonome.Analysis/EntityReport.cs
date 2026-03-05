namespace Autonome.Analysis;

public sealed class AnalysisResult
{
    public required int TotalTicks { get; init; }
    public required int TotalActionEvents { get; init; }
    public required int TotalSnapshots { get; init; }
    public required int EmbodiedCount { get; init; }
    public required int UnembodiedCount { get; init; }
    public required List<EntityReport> Entities { get; init; }
}

public sealed class EntityReport
{
    public required string Id { get; init; }
    public required bool Embodied { get; init; }
    public required int TotalActions { get; init; }
    public required int UniqueActions { get; init; }
    public required int FirstTick { get; init; }
    public required int LastTick { get; init; }

    // Action breakdown
    public required List<ActionCount> ActionBreakdown { get; init; }

    // Score stats
    public required float AvgScore { get; init; }
    public required float MinScore { get; init; }
    public required float MaxScore { get; init; }
    public required float StdDevScore { get; init; }

    // Score trajectory by quarter
    public required List<QuarterStats> ScoreByQuarter { get; init; }

    // Property snapshots
    public required Dictionary<string, float> FirstProperties { get; init; }
    public required Dictionary<string, float> LastProperties { get; init; }
    public required Dictionary<string, float> PropertyDeltas { get; init; }

    // Property trajectory from world snapshots
    public required List<PropertySnapshot> PropertyTrajectory { get; init; }

    // Decision margins
    public required float AvgMargin { get; init; }
    public required float MinMargin { get; init; }
    public required float MaxMargin { get; init; }
    public required int CloseCallCount { get; init; }
    public required List<CloseCall> CloseCalls { get; init; }

    // Consecutive runs
    public required List<ActionRun> ConsecutiveRuns { get; init; }

    // Runner-up analysis
    public required List<RunnerUpEntry> RunnerUps { get; init; }

    // Critical property alerts
    public required int CriticalAlertTicks { get; init; }
    public required List<CriticalAlert> CriticalAlerts { get; init; }
}

public sealed record ActionCount(string ActionId, int Count, float Percentage);

public sealed record QuarterStats(
    string Label,
    int StartTick,
    int EndTick,
    int Count,
    float AvgScore,
    float MinScore,
    float MaxScore,
    float TrendDelta);

public sealed record PropertySnapshot(int Tick, Dictionary<string, float> Properties);

public sealed record CloseCall(
    int Tick,
    string WinnerAction,
    float WinnerScore,
    string RunnerUpAction,
    float RunnerUpScore,
    float Margin);

public sealed record ActionRun(string ActionId, int StartTick, int EndTick, int Length);

public sealed record RunnerUpEntry(string ActionId, int TimesAsCandidate, int TimesChosen);

public sealed record CriticalAlert(int Tick, string ActionChosen, List<string> Alerts);
