namespace Autonome.Core.Model;

/// <summary>
/// A single keyframe on a response curve, inspired by Unity's AnimationCurve.
/// Tangent slopes control the shape between keyframes via cubic Hermite interpolation.
/// </summary>
public sealed record Keyframe(
    float Time,
    float Value,
    float InTangent = 0f,
    float OutTangent = 0f
);
