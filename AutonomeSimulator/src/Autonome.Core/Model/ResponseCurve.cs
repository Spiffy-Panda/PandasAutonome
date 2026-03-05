namespace Autonome.Core.Model;

/// <summary>
/// A piecewise cubic Hermite curve defined by keyframes in [0,1] -> [0,1] space.
/// Uses tangent slopes (like Unity AnimationCurve / Godot Curve) instead of Bezier handles.
/// </summary>
public sealed record ResponseCurve(List<Keyframe> Keys);

/// <summary>
/// A property response entry in an action definition.
/// References a curve (resolved at load time) and a magnitude multiplier.
/// </summary>
public sealed record PropertyResponse(
    ResponseCurve Curve,
    float Magnitude
);
