using Autonome.Core.Simulation;

namespace Autonome.Analysis;

public static class SimulationAnalyzer
{
    private const float CloseCallThreshold = 0.05f;
    private const float CriticalPropertyThreshold = 0.0f;
    private const int MaxCloseCallsReported = 25;
    private const int MaxCriticalAlertsReported = 30;

    public static AnalysisResult Analyze(SimulationResult result)
    {
        var byEntity = result.ActionEvents
            .GroupBy(e => e.AutonomeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var totalTicks = result.ActionEvents.Count > 0
            ? result.ActionEvents.Max(e => e.Tick)
            : 0;

        var entities = new List<EntityReport>();
        foreach (var (id, events) in byEntity.OrderBy(kv => kv.Key))
        {
            entities.Add(AnalyzeEntity(id, events, result.Snapshots, totalTicks));
        }

        return new AnalysisResult
        {
            TotalTicks = totalTicks,
            TotalActionEvents = result.ActionEvents.Count,
            TotalSnapshots = result.Snapshots.Count,
            EmbodiedCount = entities.Count(e => e.Embodied),
            UnembodiedCount = entities.Count(e => !e.Embodied),
            Entities = entities
        };
    }

    private static EntityReport AnalyzeEntity(
        string id,
        List<ActionEvent> events,
        List<WorldSnapshot> snapshots,
        int totalTicks)
    {
        var embodied = events[0].Embodied;
        var scores = events.Select(e => e.Score).ToList();

        // Action breakdown
        var actionGroups = events.GroupBy(e => e.ActionId)
            .Select(g => new ActionCount(g.Key, g.Count(), 100f * g.Count() / events.Count))
            .OrderByDescending(a => a.Count)
            .ToList();

        // Score stats
        float avgScore = scores.Average();
        float stdDev = MathF.Sqrt(scores.Select(s => (s - avgScore) * (s - avgScore)).Average());

        // Score by quarter
        var quarters = BuildQuarters(totalTicks);
        var quarterStats = ComputeQuarterStats(events, quarters);

        // Property snapshots
        var firstProps = new Dictionary<string, float>(events[0].PropertySnapshot);
        var lastProps = new Dictionary<string, float>(events[^1].PropertySnapshot);
        var deltas = new Dictionary<string, float>();
        foreach (var key in firstProps.Keys.Union(lastProps.Keys))
        {
            firstProps.TryGetValue(key, out float first);
            lastProps.TryGetValue(key, out float last);
            deltas[key] = last - first;
        }

        // Property trajectory from world snapshots
        var trajectory = new List<PropertySnapshot>();
        foreach (var snap in snapshots)
        {
            if (snap.EntityProperties.TryGetValue(id, out var props))
            {
                trajectory.Add(new PropertySnapshot(snap.Tick, new Dictionary<string, float>(props)));
            }
        }

        // Decision margins
        var margins = ComputeMargins(events);
        var closeCalls = ComputeCloseCalls(events);

        // Consecutive runs
        var runs = ComputeConsecutiveRuns(events);

        // Runner-up analysis
        var runnerUps = ComputeRunnerUps(events);

        // Critical alerts
        var (alertCount, alerts) = ComputeCriticalAlerts(events);

        return new EntityReport
        {
            Id = id,
            Embodied = embodied,
            TotalActions = events.Count,
            UniqueActions = actionGroups.Count,
            FirstTick = events[0].Tick,
            LastTick = events[^1].Tick,
            ActionBreakdown = actionGroups,
            AvgScore = avgScore,
            MinScore = scores.Min(),
            MaxScore = scores.Max(),
            StdDevScore = stdDev,
            ScoreByQuarter = quarterStats,
            FirstProperties = firstProps,
            LastProperties = lastProps,
            PropertyDeltas = deltas,
            PropertyTrajectory = trajectory,
            AvgMargin = margins.Count > 0 ? margins.Average() : 0,
            MinMargin = margins.Count > 0 ? margins.Min() : 0,
            MaxMargin = margins.Count > 0 ? margins.Max() : 0,
            CloseCallCount = closeCalls.Count,
            CloseCalls = closeCalls.Take(MaxCloseCallsReported).ToList(),
            ConsecutiveRuns = runs,
            RunnerUps = runnerUps,
            CriticalAlertTicks = alertCount,
            CriticalAlerts = alerts
        };
    }

    private static List<(string Label, int Start, int End)> BuildQuarters(int totalTicks)
    {
        int q = Math.Max(1, totalTicks / 4);
        var quarters = new List<(string, int, int)>();
        for (int i = 0; i < 4; i++)
        {
            int start = i * q + 1;
            int end = (i == 3) ? totalTicks : (i + 1) * q;
            quarters.Add(($"Q{i + 1} (tick {start}-{end})", start, end));
        }
        return quarters;
    }

    private static List<QuarterStats> ComputeQuarterStats(
        List<ActionEvent> events,
        List<(string Label, int Start, int End)> quarters)
    {
        var stats = new List<QuarterStats>();
        float prevAvg = 0;
        bool hasPrev = false;

        foreach (var (label, start, end) in quarters)
        {
            var qEvents = events.Where(e => e.Tick >= start && e.Tick <= end).ToList();
            if (qEvents.Count == 0)
            {
                stats.Add(new QuarterStats(label, start, end, 0, 0, 0, 0, 0));
                continue;
            }

            var qScores = qEvents.Select(e => e.Score).ToList();
            float avg = qScores.Average();
            float delta = hasPrev ? avg - prevAvg : 0;

            stats.Add(new QuarterStats(label, start, end, qEvents.Count,
                avg, qScores.Min(), qScores.Max(), delta));

            prevAvg = avg;
            hasPrev = true;
        }

        return stats;
    }

    private static List<float> ComputeMargins(List<ActionEvent> events)
    {
        var margins = new List<float>();
        foreach (var ev in events)
        {
            if (ev.TopCandidates.Count >= 2)
                margins.Add(ev.TopCandidates[0].Score - ev.TopCandidates[1].Score);
        }
        return margins;
    }

    private static List<CloseCall> ComputeCloseCalls(List<ActionEvent> events)
    {
        var calls = new List<CloseCall>();
        foreach (var ev in events)
        {
            if (ev.TopCandidates.Count < 2) continue;
            float margin = ev.TopCandidates[0].Score - ev.TopCandidates[1].Score;
            if (margin < CloseCallThreshold)
            {
                calls.Add(new CloseCall(
                    ev.Tick,
                    ev.TopCandidates[0].ActionId,
                    ev.TopCandidates[0].Score,
                    ev.TopCandidates[1].ActionId,
                    ev.TopCandidates[1].Score,
                    margin));
            }
        }
        return calls.OrderBy(c => c.Margin).ToList();
    }

    private static List<ActionRun> ComputeConsecutiveRuns(List<ActionEvent> events)
    {
        if (events.Count == 0) return [];

        var runs = new List<ActionRun>();
        string currentAction = events[0].ActionId;
        int startTick = events[0].Tick;
        int count = 1;

        for (int i = 1; i < events.Count; i++)
        {
            if (events[i].ActionId == currentAction)
            {
                count++;
            }
            else
            {
                if (count >= 2)
                    runs.Add(new ActionRun(currentAction, startTick, events[i - 1].Tick, count));
                currentAction = events[i].ActionId;
                startTick = events[i].Tick;
                count = 1;
            }
        }
        if (count >= 2)
            runs.Add(new ActionRun(currentAction, startTick, events[^1].Tick, count));

        return runs.OrderByDescending(r => r.Length).ToList();
    }

    private static List<RunnerUpEntry> ComputeRunnerUps(List<ActionEvent> events)
    {
        var chosen = events.GroupBy(e => e.ActionId)
            .ToDictionary(g => g.Key, g => g.Count());

        var candidateCounts = new Dictionary<string, int>();
        foreach (var ev in events)
        {
            // Count appearances in topCandidates (excluding rank 0 which is the winner)
            foreach (var cand in ev.TopCandidates.Skip(1))
            {
                candidateCounts.TryGetValue(cand.ActionId, out int c);
                candidateCounts[cand.ActionId] = c + 1;
            }
        }

        return candidateCounts
            .Select(kv => new RunnerUpEntry(kv.Key, kv.Value, chosen.GetValueOrDefault(kv.Key, 0)))
            .OrderByDescending(r => r.TimesAsCandidate)
            .ToList();
    }

    private static (int Count, List<CriticalAlert> Alerts) ComputeCriticalAlerts(List<ActionEvent> events)
    {
        var alerts = new List<CriticalAlert>();

        foreach (var ev in events)
        {
            var issues = new List<string>();
            foreach (var (prop, val) in ev.PropertySnapshot)
            {
                // Skip gold/resource-type properties (values typically > 1)
                if (val > 1.0f) continue;

                if (val <= CriticalPropertyThreshold)
                    issues.Add($"{prop}_ZERO");
            }

            if (issues.Count > 0)
                alerts.Add(new CriticalAlert(ev.Tick, ev.ActionId, issues));
        }

        int totalCount = alerts.Count;
        return (totalCount, alerts.Take(MaxCriticalAlertsReported).ToList());
    }
}
