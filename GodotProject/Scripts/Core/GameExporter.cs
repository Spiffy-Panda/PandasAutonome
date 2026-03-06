using Godot;
using Autonome.Analysis;
using Autonome.History;

namespace AutonomeSim.Core;

/// <summary>
/// Listens for Escape key. On press, exports the same analysis output
/// as a CLI headless run (--analyze), then quits the game.
/// </summary>
public partial class GameExporter : Node
{
	private SimulationBridge _bridge = null!;

	public override void _Ready()
	{
		_bridge = GetNode<SimulationBridge>("/root/Main/SimulationBridge");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
		{
			GetViewport().SetInputAsHandled();
			ExportAndQuit();
		}
	}

	private void ExportAndQuit()
	{
		if (!_bridge.IsLoaded)
		{
			GD.Print("GameExporter: simulation not loaded, quitting without export.");
			GetTree().Quit();
			return;
		}

		var result = _bridge.GetSimulationResult();
		var loadResult = _bridge.GetLoadResult();

		if (result.ActionEvents.Count == 0)
		{
			GD.Print("GameExporter: no ticks recorded, quitting without export.");
			GetTree().Quit();
			return;
		}

		// Take a final snapshot
		result.Snapshots.Add(
			Autonome.Core.Simulation.SimulationRunner.TakeSnapshot(
				_bridge.World, _bridge.Profiles));

		try
		{
			// Build output directory: output/analysis/{timestamp}/
			var projectDir = ProjectSettings.GlobalizePath("res://");
			var parentDir = System.IO.Path.GetDirectoryName(projectDir.TrimEnd('/', '\\')) ?? projectDir;
			var analysisDir = System.IO.Path.Combine(parentDir, "output", "analysis");
			var stamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
			var runDir = System.IO.Path.Combine(analysisDir, stamp);
			System.IO.Directory.CreateDirectory(runDir);

			// Write metadata
			var metaJson = System.Text.Json.JsonSerializer.Serialize(new
			{
				dataPath = _bridge.ResolvedDataPath,
				source = "godot_game",
				ticks = _bridge.CurrentTick,
				gameTime = _bridge.CurrentGameTime,
			});
			System.IO.File.WriteAllText(System.IO.Path.Combine(runDir, "meta.json"), metaJson);

			// Write simulation result (simulation_result.json)
			var simOutputPath = System.IO.Path.Combine(runDir, "simulation_result.json");
			HistoryExporter.Export(result, simOutputPath);

			// Run analysis
			var analysisResult = SimulationAnalyzer.Analyze(result);
			ReportWriter.WriteToDir(analysisResult, runDir);

			// Inventory analysis
			if (loadResult != null)
			{
				var inventoryResult = InventoryAnalyzer.Analyze(
					result,
					loadResult.Actions,
					loadResult.Locations,
					loadResult.Events);
				ReportWriter.WriteInventory(inventoryResult, runDir);
			}

			GD.Print($"GameExporter: analysis exported to {runDir}/");
			GD.Print($"  {result.ActionEvents.Count} action events, {result.Snapshots.Count} snapshots");
			GD.Print($"  Tick {_bridge.CurrentTick} ({_bridge.CurrentGameTime})");
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"GameExporter: export failed — {ex.Message}");
			GD.PrintErr(ex.StackTrace);
		}

		GetTree().Quit();
	}
}
