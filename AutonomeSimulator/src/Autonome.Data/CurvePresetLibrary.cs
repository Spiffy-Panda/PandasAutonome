using System.Text.Json;
using Autonome.Core.Model;

namespace Autonome.Data;

/// <summary>
/// Loads curve presets from curves.json and resolves preset names to ResponseCurve objects.
/// </summary>
public class CurvePresetLibrary
{
    private readonly Dictionary<string, ResponseCurve> _presets = new();

    public void LoadFromFile(string path)
    {
        if (!File.Exists(path)) return;

        var json = File.ReadAllText(path);
        var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("presets", out var presets)) return;

        foreach (var preset in presets.EnumerateObject())
        {
            var keys = new List<Keyframe>();
            if (preset.Value.TryGetProperty("keys", out var keysArr))
            {
                foreach (var key in keysArr.EnumerateArray())
                {
                    float time = key.GetProperty("time").GetSingle();
                    float value = key.GetProperty("value").GetSingle();
                    float inTangent = key.TryGetProperty("inTangent", out var inT) ? inT.GetSingle() : 0f;
                    float outTangent = key.TryGetProperty("outTangent", out var outT) ? outT.GetSingle() : 0f;

                    keys.Add(new Keyframe(time, value, inTangent, outTangent));
                }
            }

            _presets[preset.Name] = new ResponseCurve(keys);
        }
    }

    public ResponseCurve? Resolve(string presetName) =>
        _presets.TryGetValue(presetName, out var curve) ? curve : null;

    public bool HasPreset(string name) => _presets.ContainsKey(name);

    public IReadOnlyDictionary<string, ResponseCurve> All => _presets;
}
