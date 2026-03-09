using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Autonome.Analysis;

public static class ReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Creates a timestamped subdirectory and writes all analysis reports into it.
    /// Returns the full path to the created run directory.
    /// </summary>
    public static string Write(AnalysisResult result, string outputDir)
    {
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var runDir = Path.Combine(outputDir, stamp);
        Directory.CreateDirectory(runDir);
        WriteToDir(result, runDir);
        return runDir;
    }

    /// <summary>
    /// Writes all analysis reports into an existing directory.
    /// </summary>
    public static void WriteToDir(AnalysisResult result, string runDir)
    {
        WriteTextReports(result, runDir);
        WriteJsonReport(result, Path.Combine(runDir, "report.json"));
        WriteBalanceVerification(result, Path.Combine(runDir, "balance.md"));

        // Per-entity files for detailed drill-down
        var entitiesDir = Path.Combine(runDir, "entities");
        Directory.CreateDirectory(entitiesDir);
        foreach (var entity in result.Entities)
        {
            WriteEntityText(entity, Path.Combine(entitiesDir, $"{entity.Id}.txt"));
        }
    }

    /// <summary>
    /// Writes inventory analysis report as JSON.
    /// </summary>
    public static void WriteInventory(InventoryReport inventory, string runDir)
    {
        File.WriteAllText(
            Path.Combine(runDir, "inventory.json"),
            JsonSerializer.Serialize(inventory, JsonOptions));
    }

    private static void WriteJsonReport(AnalysisResult result, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonOptions));
    }

    /// <summary>
    /// Writes analysis into topic-focused files for easy automated consumption.
    /// </summary>
    private static void WriteTextReports(AnalysisResult result, string runDir)
    {
        var embodied = result.Entities.Where(e => e.Embodied).OrderByDescending(e => e.TotalActions).ToList();
        var unembodied = result.Entities.Where(e => !e.Embodied).OrderByDescending(e => e.TotalActions).ToList();

        WriteSummary(result, embodied, unembodied, Path.Combine(runDir, "summary.txt"));
        WriteActionBreakdown(embodied, unembodied, Path.Combine(runDir, "action_breakdown.txt"));
        WriteScoreStats(embodied, unembodied, Path.Combine(runDir, "score_stats.txt"));
        WritePropertyChanges(embodied, unembodied, Path.Combine(runDir, "property_changes.txt"));
        WriteDecisionMargins(embodied, unembodied, Path.Combine(runDir, "decision_margins.txt"));
        WriteConsecutiveRuns(embodied, unembodied, Path.Combine(runDir, "consecutive_runs.txt"));
        WriteRunnerUps(embodied, unembodied, Path.Combine(runDir, "runner_ups.txt"));
        WriteCriticalAlerts(embodied, unembodied, Path.Combine(runDir, "critical_alerts.txt"));
    }

    private static void WriteSummary(AnalysisResult result, List<EntityReport> embodied, List<EntityReport> unembodied, string path)
    {
        var sb = new StringBuilder();
        WriteHeader(sb, result);

        if (unembodied.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("  UNEMBODIED AUTONOMES");
            sb.AppendLine(new string('=', 80));
            WriteComparisonTable(sb, unembodied);
        }

        if (embodied.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(new string('=', 80));
            sb.AppendLine("  EMBODIED AUTONOMES");
            sb.AppendLine(new string('=', 80));
            WriteComparisonTable(sb, embodied);
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteActionBreakdown(List<EntityReport> embodied, List<EntityReport> unembodied, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== ACTION BREAKDOWN ===");

        foreach (var group in new[] { ("UNEMBODIED", unembodied), ("EMBODIED", embodied) })
        {
            if (group.Item2.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine($"  [{group.Item1}]");
            foreach (var e in group.Item2)
            {
                sb.AppendLine();
                sb.AppendLine($"  --- {e.Id} --- (total={e.TotalActions}, unique={e.UniqueActions})");
                foreach (var a in e.ActionBreakdown)
                    sb.AppendLine($"    {a.ActionId,-30} {a.Count,5} ({a.Percentage,5:F1}%)");
            }
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteScoreStats(List<EntityReport> embodied, List<EntityReport> unembodied, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== SCORE STATISTICS & TRAJECTORIES ===");

        foreach (var group in new[] { ("UNEMBODIED", unembodied), ("EMBODIED", embodied) })
        {
            if (group.Item2.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine($"  [{group.Item1}]");
            foreach (var e in group.Item2)
            {
                sb.AppendLine();
                sb.AppendLine($"  --- {e.Id} ---");
                sb.AppendLine($"  Scores: avg={e.AvgScore:F4} min={e.MinScore:F4} max={e.MaxScore:F4} stddev={e.StdDevScore:F4}");
                sb.AppendLine();
                sb.AppendLine("  Score Trajectory by Quarter:");
                foreach (var q in e.ScoreByQuarter)
                {
                    string trend = q.TrendDelta switch
                    {
                        > 0.02f => $"RISING +{q.TrendDelta:F3}",
                        < -0.02f => $"FALLING {q.TrendDelta:F3}",
                        _ => $"STABLE {(q.TrendDelta >= 0 ? "+" : "")}{q.TrendDelta:F3}"
                    };
                    sb.AppendLine($"    {q.Label,-22} n={q.Count,3} avg={q.AvgScore:F4} min={q.MinScore:F4} max={q.MaxScore:F4} [{trend}]");
                }
            }
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void WritePropertyChanges(List<EntityReport> embodied, List<EntityReport> unembodied, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== PROPERTY CHANGES & TRAJECTORIES ===");

        foreach (var group in new[] { ("UNEMBODIED", unembodied), ("EMBODIED", embodied) })
        {
            if (group.Item2.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine($"  [{group.Item1}]");
            foreach (var e in group.Item2)
            {
                sb.AppendLine();
                sb.AppendLine($"  --- {e.Id} ---");
                sb.AppendLine("  Property Changes (first -> last):");
                foreach (var (prop, delta) in e.PropertyDeltas.OrderBy(kv => kv.Key))
                {
                    e.FirstProperties.TryGetValue(prop, out float first);
                    e.LastProperties.TryGetValue(prop, out float last);
                    sb.AppendLine($"    {prop,-18} {FormatPropVal(first),10} -> {FormatPropVal(last),10}  ({delta:+0.000;-0.000})");
                }

                if (e.PropertyTrajectory.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("  Property Trajectory (world snapshots):");
                    var allProps = e.PropertyTrajectory.SelectMany(s => s.Properties.Keys).Distinct().OrderBy(k => k).ToList();
                    sb.Append($"    {"tick",6}");
                    foreach (var p in allProps) sb.Append($"  {p,12}");
                    sb.AppendLine();

                    foreach (var snap in e.PropertyTrajectory)
                    {
                        sb.Append($"    {snap.Tick,6}");
                        foreach (var p in allProps)
                        {
                            snap.Properties.TryGetValue(p, out float val);
                            sb.Append($"  {FormatPropVal(val),12}");
                        }
                        sb.AppendLine();
                    }
                }
            }
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteDecisionMargins(List<EntityReport> embodied, List<EntityReport> unembodied, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== DECISION MARGINS & CLOSE CALLS ===");

        foreach (var group in new[] { ("UNEMBODIED", unembodied), ("EMBODIED", embodied) })
        {
            if (group.Item2.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine($"  [{group.Item1}]");
            foreach (var e in group.Item2)
            {
                sb.AppendLine();
                sb.AppendLine($"  --- {e.Id} ---");
                sb.AppendLine($"  Margins: avg={e.AvgMargin:F4} min={e.MinMargin:F4} max={e.MaxMargin:F4} closeCalls={e.CloseCallCount}");
                if (e.CloseCalls.Count > 0)
                {
                    sb.AppendLine("  Closest Calls:");
                    foreach (var cc in e.CloseCalls.Take(10))
                        sb.AppendLine($"    tick {cc.Tick,5}: {cc.WinnerAction} ({cc.WinnerScore:F4}) vs {cc.RunnerUpAction} ({cc.RunnerUpScore:F4}) margin={cc.Margin:F6}");
                }
            }
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteConsecutiveRuns(List<EntityReport> embodied, List<EntityReport> unembodied, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== CONSECUTIVE ACTION RUNS (2+) ===");

        foreach (var group in new[] { ("UNEMBODIED", unembodied), ("EMBODIED", embodied) })
        {
            if (group.Item2.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine($"  [{group.Item1}]");
            foreach (var e in group.Item2)
            {
                if (e.ConsecutiveRuns.Count == 0) continue;
                sb.AppendLine();
                sb.AppendLine($"  --- {e.Id} ---");
                foreach (var run in e.ConsecutiveRuns)
                    sb.AppendLine($"    {run.ActionId,-30} {run.Length}x (tick {run.StartTick}-{run.EndTick})");
            }
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteRunnerUps(List<EntityReport> embodied, List<EntityReport> unembodied, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== RUNNER-UP ANALYSIS ===");

        foreach (var group in new[] { ("UNEMBODIED", unembodied), ("EMBODIED", embodied) })
        {
            if (group.Item2.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine($"  [{group.Item1}]");
            foreach (var e in group.Item2)
            {
                if (e.RunnerUps.Count == 0) continue;
                sb.AppendLine();
                sb.AppendLine($"  --- {e.Id} ---");
                sb.AppendLine($"    {"Action",-30} {"As Candidate",13} {"Times Chosen",13}");
                foreach (var ru in e.RunnerUps)
                    sb.AppendLine($"    {ru.ActionId,-30} {ru.TimesAsCandidate,13} {ru.TimesChosen,13}");
            }
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteCriticalAlerts(List<EntityReport> embodied, List<EntityReport> unembodied, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== CRITICAL PROPERTY ALERTS ===");

        foreach (var group in new[] { ("UNEMBODIED", unembodied), ("EMBODIED", embodied) })
        {
            if (group.Item2.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine($"  [{group.Item1}]");
            foreach (var e in group.Item2)
            {
                sb.AppendLine();
                sb.AppendLine($"  --- {e.Id} --- ({e.CriticalAlertTicks} ticks with zero-value properties)");
                if (e.CriticalAlerts.Count > 0)
                {
                    foreach (var alert in e.CriticalAlerts.Take(10))
                        sb.AppendLine($"    tick {alert.Tick,5}: [{string.Join(", ", alert.Alerts)}] -> {alert.ActionChosen}");
                    if (e.CriticalAlertTicks > 10)
                        sb.AppendLine($"    ... and {e.CriticalAlertTicks - 10} more");
                }
            }
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteHeader(StringBuilder sb, AnalysisResult result)
    {
        sb.AppendLine("=== AUTONOME SIMULATION ANALYSIS ===");
        sb.AppendLine();
        sb.AppendLine($"  Total ticks:          {result.TotalTicks}");
        sb.AppendLine($"  Total action events:  {result.TotalActionEvents}");
        sb.AppendLine($"  World snapshots:      {result.TotalSnapshots}");
        sb.AppendLine($"  Embodied entities:    {result.EmbodiedCount}");
        sb.AppendLine($"  Unembodied entities:  {result.UnembodiedCount}");
    }

    private static void WriteComparisonTable(StringBuilder sb, List<EntityReport> entities)
    {
        sb.AppendLine();
        sb.AppendLine($"  {"ID",-30} {"Acts",5} {"Uniq",5} {"AvgScr",8} {"AvgMrg",8} {"Close",6} {"Dominant Action",-28} {"Dom%",5}");
        sb.AppendLine($"  {new string('-', 30)} {new string('-', 5)} {new string('-', 5)} {new string('-', 8)} {new string('-', 8)} {new string('-', 6)} {new string('-', 28)} {new string('-', 5)}");

        foreach (var e in entities.OrderByDescending(e => e.TotalActions))
        {
            var dominant = e.ActionBreakdown.FirstOrDefault();
            sb.AppendLine($"  {e.Id,-30} {e.TotalActions,5} {e.UniqueActions,5} {e.AvgScore,8:F3} {e.AvgMargin,8:F3} {e.CloseCallCount,6} {dominant?.ActionId ?? "N/A",-28} {dominant?.Percentage ?? 0,5:F1}");
        }
    }

    private static void WriteEntityText(EntityReport e, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"  --- {e.Id} ---");
        sb.AppendLine($"  Embodied: {e.Embodied} | Actions: {e.TotalActions} | Unique: {e.UniqueActions} | Ticks: {e.FirstTick}-{e.LastTick}");
        sb.AppendLine();

        sb.AppendLine("  Action Breakdown:");
        foreach (var a in e.ActionBreakdown)
            sb.AppendLine($"    {a.ActionId,-30} {a.Count,5} ({a.Percentage,5:F1}%)");

        sb.AppendLine();
        sb.AppendLine($"  Scores: avg={e.AvgScore:F4} min={e.MinScore:F4} max={e.MaxScore:F4} stddev={e.StdDevScore:F4}");

        sb.AppendLine();
        sb.AppendLine("  Score Trajectory by Quarter:");
        foreach (var q in e.ScoreByQuarter)
        {
            string trend = q.TrendDelta switch
            {
                > 0.02f => $"RISING +{q.TrendDelta:F3}",
                < -0.02f => $"FALLING {q.TrendDelta:F3}",
                _ => $"STABLE {(q.TrendDelta >= 0 ? "+" : "")}{q.TrendDelta:F3}"
            };
            sb.AppendLine($"    {q.Label,-22} n={q.Count,3} avg={q.AvgScore:F4} min={q.MinScore:F4} max={q.MaxScore:F4} [{trend}]");
        }

        sb.AppendLine();
        sb.AppendLine("  Property Changes (first -> last):");
        foreach (var (prop, delta) in e.PropertyDeltas.OrderBy(kv => kv.Key))
        {
            e.FirstProperties.TryGetValue(prop, out float first);
            e.LastProperties.TryGetValue(prop, out float last);
            sb.AppendLine($"    {prop,-18} {FormatPropVal(first),10} -> {FormatPropVal(last),10}  ({delta:+0.000;-0.000})");
        }

        if (e.PropertyTrajectory.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  Property Trajectory (world snapshots):");
            var allProps = e.PropertyTrajectory.SelectMany(s => s.Properties.Keys).Distinct().OrderBy(k => k).ToList();
            sb.Append($"    {"tick",6}");
            foreach (var p in allProps) sb.Append($"  {p,12}");
            sb.AppendLine();
            foreach (var snap in e.PropertyTrajectory)
            {
                sb.Append($"    {snap.Tick,6}");
                foreach (var p in allProps)
                {
                    snap.Properties.TryGetValue(p, out float val);
                    sb.Append($"  {FormatPropVal(val),12}");
                }
                sb.AppendLine();
            }
        }

        sb.AppendLine();
        sb.AppendLine($"  Decision Margins: avg={e.AvgMargin:F4} min={e.MinMargin:F4} max={e.MaxMargin:F4} closeCalls={e.CloseCallCount}");
        if (e.CloseCalls.Count > 0)
        {
            sb.AppendLine("  Closest Calls:");
            foreach (var cc in e.CloseCalls.Take(10))
                sb.AppendLine($"    tick {cc.Tick,5}: {cc.WinnerAction} ({cc.WinnerScore:F4}) vs {cc.RunnerUpAction} ({cc.RunnerUpScore:F4}) margin={cc.Margin:F6}");
        }

        if (e.ConsecutiveRuns.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  Consecutive Action Runs (2+):");
            foreach (var run in e.ConsecutiveRuns.Take(10))
                sb.AppendLine($"    {run.ActionId,-30} {run.Length}x (tick {run.StartTick}-{run.EndTick})");
        }

        if (e.RunnerUps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("  Runner-Up Analysis:");
            sb.AppendLine($"    {"Action",-30} {"As Candidate",13} {"Times Chosen",13}");
            foreach (var ru in e.RunnerUps.Take(8))
                sb.AppendLine($"    {ru.ActionId,-30} {ru.TimesAsCandidate,13} {ru.TimesChosen,13}");
        }

        sb.AppendLine();
        sb.AppendLine($"  Critical Property Alerts: {e.CriticalAlertTicks} ticks with zero-value properties");
        if (e.CriticalAlerts.Count > 0)
        {
            foreach (var alert in e.CriticalAlerts.Take(10))
                sb.AppendLine($"    tick {alert.Tick,5}: [{string.Join(", ", alert.Alerts)}] -> {alert.ActionChosen}");
            if (e.CriticalAlertTicks > 10)
                sb.AppendLine($"    ... and {e.CriticalAlertTicks - 10} more");
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static void WriteBalanceVerification(AnalysisResult result, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Balance Verification Report");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}  ");
        sb.AppendLine($"**Ticks:** {result.TotalTicks} | **Actions:** {result.TotalActionEvents} | **Entities:** {result.EmbodiedCount + result.UnembodiedCount}");
        sb.AppendLine();

        var orgs = result.Entities.Where(e => !e.Embodied).ToList();
        if (orgs.Count == 0)
        {
            sb.AppendLine("*No unembodied entities to verify.*");
            File.WriteAllText(path, sb.ToString());
            return;
        }

        // === Action Diversity ===
        sb.AppendLine("## Action Diversity");
        sb.AppendLine();
        sb.AppendLine("| Entity | Total | Unique | Dominant Action | Dom% | Status |");
        sb.AppendLine("|--------|------:|-------:|-----------------|-----:|--------|");

        var diversityIssues = new List<string>();
        foreach (var e in orgs.OrderBy(e => e.Id))
        {
            var dom = e.ActionBreakdown.FirstOrDefault();
            float domPct = dom?.Percentage ?? 0;
            string status;
            if (e.UniqueActions <= 1) status = "FAIL: single action";
            else if (domPct > 80) status = "FAIL: over-dominant";
            else if (domPct > 60) status = "WARN: leaning dominant";
            else status = "PASS";

            sb.AppendLine($"| {e.Id} | {e.TotalActions} | {e.UniqueActions} | {dom?.ActionId ?? "N/A"} | {domPct:F1} | {status} |");

            if (status.StartsWith("FAIL"))
                diversityIssues.Add($"- FAIL: **{e.Id}** — {status.Substring(6)} ({dom?.ActionId} at {domPct:F1}%)");
        }
        sb.AppendLine();

        // === Property Stability ===
        sb.AppendLine("## Property Stability");
        sb.AppendLine();
        sb.AppendLine("Checks whether organic properties (0–1 range) collapse to zero or near-zero.");
        sb.AppendLine();
        sb.AppendLine("| Entity | Property | Start | End | Delta | Status |");
        sb.AppendLine("|--------|----------|------:|----:|------:|--------|");

        var stabilityIssues = new List<string>();
        foreach (var e in orgs.OrderBy(e => e.Id))
        {
            foreach (var (prop, delta) in e.PropertyDeltas.OrderBy(kv => kv.Key))
            {
                e.FirstProperties.TryGetValue(prop, out float first);
                e.LastProperties.TryGetValue(prop, out float last);

                // Skip gold/lumber — they're resource pools, not organic properties
                if (first > 2f || last > 2f) continue;

                string status;
                if (last <= 0.01f) status = "FAIL: collapsed";
                else if (last < 0.1f) status = "WARN: critically low";
                else if (delta < -0.3f) status = "WARN: steep decline";
                else status = "PASS";

                sb.AppendLine($"| {e.Id} | {prop} | {first:F3} | {last:F3} | {delta:+0.000;-0.000} | {status} |");

                if (status.StartsWith("FAIL"))
                    stabilityIssues.Add($"- FAIL: **{e.Id}.{prop}** collapsed to {last:F3}");
                else if (status.StartsWith("WARN"))
                    stabilityIssues.Add($"- WARN: **{e.Id}.{prop}** — {status.Substring(6)} (end={last:F3})");
            }
        }
        sb.AppendLine();

        // === Gold Sustainability ===
        sb.AppendLine("## Gold Sustainability");
        sb.AppendLine();
        sb.AppendLine("| Entity | Start Gold | End Gold | Delta | Status |");
        sb.AppendLine("|--------|----------:|--------:|------:|--------|");

        var goldIssues = new List<string>();
        foreach (var e in orgs.OrderBy(e => e.Id))
        {
            e.FirstProperties.TryGetValue("gold", out float startGold);
            e.LastProperties.TryGetValue("gold", out float endGold);
            float goldDelta = endGold - startGold;

            string status;
            if (endGold <= 0) status = "FAIL: bankrupt";
            else if (endGold < startGold * 0.1f) status = "WARN: nearly depleted";
            else if (goldDelta < -startGold * 0.5f) status = "WARN: rapid depletion";
            else status = "PASS";

            sb.AppendLine($"| {e.Id} | {startGold:F0} | {endGold:F0} | {goldDelta:+0;-0} | {status} |");

            if (status.StartsWith("FAIL"))
                goldIssues.Add($"- FAIL: **{e.Id}** bankrupt (0 gold)");
            else if (status.StartsWith("WARN"))
                goldIssues.Add($"- WARN: **{e.Id}** — {status.Substring(6)} ({endGold:F0} gold remaining)");
        }
        sb.AppendLine();

        // === Cooldown Effectiveness ===
        sb.AppendLine("## Cooldown Effectiveness");
        sb.AppendLine();
        sb.AppendLine("Consecutive runs of 3+ indicate a potentially broken or missing cooldown.");
        sb.AppendLine();

        var cooldownIssues = new List<string>();
        bool anyCooldownRows = false;
        foreach (var e in orgs.OrderBy(e => e.Id))
        {
            var longRuns = e.ConsecutiveRuns.Where(r => r.Length >= 3).ToList();
            if (longRuns.Count > 0)
            {
                if (!anyCooldownRows)
                {
                    sb.AppendLine("| Entity | Action | Run Length | Ticks | Status |");
                    sb.AppendLine("|--------|--------|----------:|-------|--------|");
                    anyCooldownRows = true;
                }
                foreach (var run in longRuns)
                {
                    string status = run.Length >= 5 ? "FAIL: no cooldown" : "WARN: weak cooldown";
                    sb.AppendLine($"| {e.Id} | {run.ActionId} | {run.Length} | {run.StartTick}-{run.EndTick} | {status} |");
                    string severity = run.Length >= 5 ? "FAIL" : "WARN";
                    cooldownIssues.Add($"- {severity}: **{e.Id}** — {run.ActionId} ran {run.Length}x consecutively");
                }
            }
        }
        if (!anyCooldownRows)
            sb.AppendLine("No consecutive runs of 3+ detected. Cooldowns appear effective.");
        sb.AppendLine();

        // === Critical Alerts ===
        sb.AppendLine("## Critical Property Alerts");
        sb.AppendLine();

        var alertOrgs = orgs.Where(e => e.CriticalAlertTicks > 0).ToList();
        if (alertOrgs.Count > 0)
        {
            sb.AppendLine("| Entity | Alert Ticks | % of Lifetime | Status |");
            sb.AppendLine("|--------|----------:|-------------:|--------|");
            foreach (var e in alertOrgs.OrderByDescending(e => e.CriticalAlertTicks))
            {
                int lifetime = e.LastTick - e.FirstTick;
                float alertPct = lifetime > 0 ? (float)e.CriticalAlertTicks / lifetime * 100 : 0;
                string status = alertPct > 50 ? "FAIL: chronic" : alertPct > 20 ? "WARN: frequent" : "PASS";
                sb.AppendLine($"| {e.Id} | {e.CriticalAlertTicks} | {alertPct:F1}% | {status} |");
            }
        }
        else
        {
            sb.AppendLine("No critical property alerts recorded.");
        }
        sb.AppendLine();

        // === Overall Verdict ===
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Overall Verdict");
        sb.AppendLine();

        var allIssues = diversityIssues.Concat(stabilityIssues).Concat(goldIssues).Concat(cooldownIssues).ToList();
        int failCount = allIssues.Count(i => i.Contains("FAIL"));
        int warnCount = allIssues.Count(i => i.Contains("WARN"));

        if (failCount == 0 && warnCount == 0)
        {
            sb.AppendLine("**BALANCED** — All checks passed. No action dominance, property collapse, or gold depletion detected.");
        }
        else if (failCount == 0)
        {
            sb.AppendLine($"**MOSTLY BALANCED** — {warnCount} warning(s), no failures.");
        }
        else
        {
            sb.AppendLine($"**IMBALANCED** — {failCount} failure(s) and {warnCount} warning(s) detected.");
        }

        if (allIssues.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Issues");
            sb.AppendLine();
            foreach (var issue in allIssues)
                sb.AppendLine(issue);
        }

        File.WriteAllText(path, sb.ToString());
    }

    private static string FormatPropVal(float val)
    {
        if (val == 0f) return "0";
        if (MathF.Abs(val) >= 2f) return val.ToString("F0");
        return val.ToString("F3");
    }
}
