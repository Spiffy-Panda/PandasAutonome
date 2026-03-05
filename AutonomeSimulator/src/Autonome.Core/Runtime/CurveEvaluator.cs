using Autonome.Core.Model;

namespace Autonome.Core.Runtime;

/// <summary>
/// Evaluates piecewise cubic Hermite response curves.
/// Hot path — called per-property per-action per-Autonome per-tick.
/// Uses tangent slopes (like Unity AnimationCurve / Godot Curve).
/// </summary>
public static class CurveEvaluator
{
    public static float Evaluate(ResponseCurve curve, float x)
    {
        var keys = curve.Keys;
        if (keys.Count == 0) return 0f;
        if (keys.Count == 1) return Math.Clamp(keys[0].Value, 0f, 1f);

        x = Math.Clamp(x, 0f, 1f);

        // Find segment: linear scan (N is typically 2-4, faster than binary search)
        int i = 0;
        for (; i < keys.Count - 2; i++)
        {
            if (x < keys[i + 1].Time) break;
        }

        var k0 = keys[i];
        var k1 = keys[i + 1];

        float segmentWidth = k1.Time - k0.Time;
        if (segmentWidth <= 0f) return Math.Clamp(k0.Value, 0f, 1f);

        float t = (x - k0.Time) / segmentWidth;

        float y;
        if (k0.OutTangent == 0f && k1.InTangent == 0f)
        {
            // Linear interpolation (common fast path: flat tangents)
            y = k0.Value + (k1.Value - k0.Value) * t;
        }
        else
        {
            // Cubic Hermite interpolation
            y = EvaluateHermite(k0, k1, segmentWidth, t);
        }

        return Math.Clamp(y, 0f, 1f);
    }

    private static float EvaluateHermite(Keyframe k0, Keyframe k1, float segmentWidth, float t)
    {
        // Scale tangent slopes by segment width to get Hermite tangent vectors
        float m0 = k0.OutTangent * segmentWidth;
        float m1 = k1.InTangent * segmentWidth;

        float t2 = t * t;
        float t3 = t2 * t;

        // Hermite basis functions
        float h00 = 2f * t3 - 3f * t2 + 1f;
        float h10 = t3 - 2f * t2 + t;
        float h01 = -2f * t3 + 3f * t2;
        float h11 = t3 - t2;

        return h00 * k0.Value + h10 * m0 + h01 * k1.Value + h11 * m1;
    }
}
