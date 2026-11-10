namespace InsertCoin.Core;

/// <summary>
/// Whether a figure has the shape a new companion's arrival was made for: feet on the floor at rest, legs and body in a
/// person's proportions, tall enough to peek round the cabinet and not so tall that bending the knees can't keep the
/// head below its top. One that hasn't appears on the stool under a flash. Measured at rest, in metres above the
/// model's origin (its feet, if it stands on it).
/// </summary>
public static class ArrivalFit
{
    private const float CabinetTop = 1.33f;     // the height the head stays under while they are out of sight

    /// <param name="head">The head joint's height.</param>
    /// <param name="hips">The hips joint's height.</param>
    /// <param name="ankle">The ankle joint's height: a model not standing on its origin has it far from the floor.</param>
    /// <param name="leg">Hip joint to ankle.</param>
    /// <param name="shoulders">Shoulder joint to shoulder joint.</param>
    public static bool Fits(float head, float hips, float ankle, float leg, float shoulders) =>
        ankle is >= 0 and <= 0.25f
        && head is >= 0.75f and <= 1.95f
        && head - hips > 0.2f
        && leg > 0
        && leg / (head - hips) is >= 0.9f and <= 2.4f
        && shoulders / leg is >= 0.15f and <= 0.6f
        && head - CabinetTop <= 0.5f * leg;
}
