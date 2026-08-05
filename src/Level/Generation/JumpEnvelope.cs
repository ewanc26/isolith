using Godot;
using Isolith.Gameplay;

namespace Isolith.Level.Generation;

/// <summary>
/// What the player can physically reach, derived from the character
/// controller's own tuning rather than from hardcoded numbers.
/// </summary>
/// <remarks>
/// This is the single source of truth for whether a generated gap is jumpable.
/// It reads <see cref="PlayerController"/>'s exported values, so retuning the
/// character automatically retunes level generation — there is no second set of
/// constants to forget to update.
///
/// The maths is the same as the table in §6 of <c>AGENTS.md</c>:
/// <code>
///   v       = √(2 · riseGravity · jumpHeight)
///   t_up    = v / riseGravity
///   t_down  = √(2 · jumpHeight / fallGravity)
///   range   = moveSpeed · (t_up + t_down)
/// </code>
/// </remarks>
public readonly struct JumpEnvelope
{
    /// <summary>
    /// Fraction of the theoretical maximum a generated gap is allowed to use.
    /// </summary>
    /// <remarks>
    /// The theoretical range assumes a perfect run-up at full speed, a jump at
    /// the exact last frame, and a landing on the very lip of the next platform.
    /// Players do none of those things reliably, and a generator that designs
    /// for a frame-perfect player produces a level that feels broken rather than
    /// hard. 72% leaves room to be imprecise and still land.
    /// </remarks>
    public const float GapSafety = 0.72f;

    /// <summary>Fraction of peak jump height a generated step-up may use.</summary>
    /// <remarks>
    /// Tighter than <see cref="GapSafety"/>: arriving at the apex with no upward
    /// velocity left means clipping the platform edge and sliding off, so a
    /// generated rise never approaches the true ceiling.
    /// </remarks>
    public const float RiseSafety = 0.62f;

    private readonly float _moveSpeed;
    private readonly float _riseGravity;
    private readonly float _fallGravity;
    private readonly float _timeUp;
    private readonly float _bounceTimeUp;

    private JumpEnvelope(
        float maxRange, float maxRise, float bounceRange, float bounceRise,
        float moveSpeed, float riseGravity, float fallGravity, float timeUp, float bounceTimeUp)
    {
        MaxRange = maxRange;
        MaxRise = maxRise;
        BounceRange = bounceRange;
        BounceRise = bounceRise;
        _moveSpeed = moveSpeed;
        _riseGravity = riseGravity;
        _fallGravity = fallGravity;
        _timeUp = timeUp;
        _bounceTimeUp = bounceTimeUp;
    }

    /// <summary>Horizontal distance a normal jump covers, before safety margin.</summary>
    public float MaxRange { get; }

    /// <summary>Height a normal jump gains, before safety margin.</summary>
    public float MaxRise { get; }

    /// <summary>Horizontal distance a bounce-pad launch covers.</summary>
    public float BounceRange { get; }

    /// <summary>Height a bounce-pad launch gains.</summary>
    public float BounceRise { get; }

    /// <summary>The largest edge-to-edge gap generation may place.</summary>
    public float SafeGap => MaxRange * GapSafety;

    /// <summary>The largest step-up generation may place.</summary>
    public float SafeRise => MaxRise * RiseSafety;

    /// <summary>The largest gap that may follow a bounce pad.</summary>
    public float SafeBounceGap => BounceRange * GapSafety;

    /// <summary>The largest rise that may follow a bounce pad.</summary>
    public float SafeBounceRise => BounceRise * RiseSafety;

    /// <summary>Derives the envelope from a live controller's tuning.</summary>
    public static JumpEnvelope From(PlayerController player) => Compute(
        player.MoveSpeed, player.JumpHeight, player.BounceHeight,
        player.RiseGravity, player.FallGravity);

    /// <summary>Derives the envelope from a controller's default tuning.</summary>
    public static JumpEnvelope Default()
    {
        // A throwaway instance rather than duplicated constants: the defaults
        // live in exactly one place, on the controller itself.
        var defaults = new PlayerController();
        JumpEnvelope envelope = From(defaults);
        defaults.Free();
        return envelope;
    }

    private static JumpEnvelope Compute(
        float moveSpeed, float jumpHeight, float bounceHeight,
        float riseGravity, float fallGravity)
    {
        (float range, float rise, float timeUp) normal = Arc(moveSpeed, jumpHeight, riseGravity, fallGravity);
        (float range, float rise, float timeUp) bounce = Arc(moveSpeed, bounceHeight, riseGravity, fallGravity);

        return new JumpEnvelope(
            normal.range, normal.rise, bounce.range, bounce.rise,
            moveSpeed, riseGravity, fallGravity, normal.timeUp, bounce.timeUp);
    }

    private static (float Range, float Rise, float TimeUp) Arc(
        float moveSpeed, float height, float riseGravity, float fallGravity)
    {
        float launch = Mathf.Sqrt(2.0f * riseGravity * height);
        float timeUp = launch / riseGravity;
        float timeDown = Mathf.Sqrt(2.0f * height / fallGravity);

        return (moveSpeed * (timeUp + timeDown), height, timeUp);
    }

    /// <summary>
    /// Height of the jump arc above the takeoff point at a given horizontal
    /// distance into the jump, clamped to the arc's own span.
    /// </summary>
    /// <remarks>
    /// Used to place things — shard height over a gap, say — so they sit
    /// inside the parabola a jump actually traces instead of at a fixed
    /// height that only happens to fit some gaps. Same two-phase model as
    /// <see cref="Arc"/>: constant horizontal speed, rise gravity climbing,
    /// fall gravity descending.
    /// </remarks>
    public float HeightAtDistance(float horizontalDistance, bool bounce = false)
    {
        float range = bounce ? BounceRange : MaxRange;
        float x = Mathf.Clamp(horizontalDistance, 0f, range);
        float timeUp = bounce ? _bounceTimeUp : _timeUp;
        float t = _moveSpeed > 0f ? x / _moveSpeed : 0f;

        if (t <= timeUp)
        {
            float launch = (bounce ? BounceRise : MaxRise) is var height && height > 0f
                ? Mathf.Sqrt(2.0f * _riseGravity * height)
                : 0f;
            return launch * t - 0.5f * _riseGravity * t * t;
        }

        float peak = bounce ? BounceRise : MaxRise;
        float fallTime = t - timeUp;
        return Mathf.Max(0f, peak - 0.5f * _fallGravity * fallTime * fallTime);
    }

    public override string ToString() =>
        $"jump {SafeGap:F1}m gap / {SafeRise:F1}m rise, " +
        $"bounce {SafeBounceGap:F1}m / {SafeBounceRise:F1}m";
}
