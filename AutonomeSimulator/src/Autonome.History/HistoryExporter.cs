using System.Text.Json;
using System.Text.Json.Serialization;
using Autonome.Core.Simulation;

namespace Autonome.History;

/// <summary>
/// Exports simulation results to JSON.
/// </summary>
public static class HistoryExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static void Export(SimulationResult result, string outputPath)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json);
    }

    public static void ExportHistory(IReadOnlyList<HistoryEntry> entries, string outputPath)
    {
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json);
    }

    public static void ExportSnapshots(IReadOnlyList<WorldSnapshotFull> snapshots, string outputPath)
    {
        var json = JsonSerializer.Serialize(snapshots, JsonOptions);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, json);
    }
}
