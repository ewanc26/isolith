using System;
using System.Collections.Generic;
using Godot;

namespace Isolith.Level.Generation;

/// <summary>
/// Decides what the next section should be, based on how the previous one went.
/// </summary>
/// <remarks>
/// This is the reactive part of generation. It holds two kinds of state:
///
/// <list type="bullet">
///   <item><b>Difficulty</b> — one 0–1 dial covering gap length, platform size,
///   and hazard density. It tracks the player's overall command of the game.</item>
///   <item><b>Per-mechanic trust</b> — a separate 0–1 value for movers,
///   crumbling platforms, and bounce pads. These move independently, because
///   "keeps falling off moving platforms" and "finds the game too easy" are
///   different problems and a single difficulty dial answers them the same way,
///   which is wrong.</item>
/// </list>
///
/// The asymmetry is deliberate throughout: trust and difficulty fall fast and
/// recover slowly. A player who just died wants immediate relief; a player who
/// cleared one section cleanly has not yet proven they want it harder. Ramping
/// up as fast as it ramps down produces oscillation — punishing, then trivial,
/// then punishing again.
/// </remarks>
public sealed class AdaptiveDirector
{
    // How far difficulty may move in a single section. Small enough that the
    // player experiences a drift rather than a step change.
    private const float MaxRise = 0.06f;
    private const float MaxFall = 0.20f;

    private const float StartingDifficulty = 0.22f;
    private const float StartingTrust = 0.30f;

    private readonly JumpEnvelope _envelope;
    private readonly RandomNumberGenerator _rng = new();
    private readonly List<SectionPerformance> _history = new();

    private float _difficulty = StartingDifficulty;
    private float _moverTrust = StartingTrust;
    private float _crumbleTrust = StartingTrust;
    private float _bounceTrust = StartingTrust;
    private float _shardRisk = 0.1f;
    private int _sectionsGenerated;

    public AdaptiveDirector(JumpEnvelope envelope, ulong seed)
    {
        _envelope = envelope;
        _rng.Seed = seed;
    }

    /// <summary>Current overall difficulty, 0–1. Surfaced in the HUD.</summary>
    public float Difficulty => _difficulty;

    /// <summary>Per-mechanic trust, for display and tests.</summary>
    public float MoverTrust => _moverTrust;
    public float CrumbleTrust => _crumbleTrust;
    public float BounceTrust => _bounceTrust;

    /// <summary>Every performance sample fed in so far, oldest first.</summary>
    public IReadOnlyList<SectionPerformance> History => _history;

    /// <summary>A one-line summary of why the last adjustment happened.</summary>
    public string LastReason { get; private set; } = "opening section";

    /// <summary>
    /// Produces the next section's recipe. Pass the previous section's
    /// performance, or <c>null</c> for the first section of a run.
    /// </summary>
    public SectionSpec Next(SectionPerformance? previous)
    {
        if (previous is null)
        {
            _sectionsGenerated++;
            return SectionSpec.Opening(_rng.Randi());
        }

        _history.Add(previous);

        Adjust(previous);
        UpdateMechanicTrust(previous);
        UpdateShardRisk(previous);

        _sectionsGenerated++;
        return Build(previous);
    }

    // -----------------------------------------------------------------------
    // Reacting
    // -----------------------------------------------------------------------

    private void Adjust(SectionPerformance previous)
    {
        float confidence = previous.Confidence;

        // Centred on 0.5: above it the player had room to spare, below it they
        // were beyond their depth.
        float delta = (confidence - 0.5f) * 0.22f;

        // Deaths get their own term on top of their effect on confidence.
        // Falling repeatedly is the one signal that should always ease things,
        // even if every other metric looked fine.
        if (previous.Deaths > 0)
            delta -= 0.06f + previous.Deaths * 0.05f;

        // Clearing gaps by a hair without dying yet: hold, don't push.
        if (previous.EdgeRatio > 0.45f && delta > 0f)
        {
            delta = 0f;
            LastReason = $"holding — {previous.EdgeRatio:P0} of landings were on the edge";
        }
        else
        {
            LastReason = previous.Deaths > 0
                ? $"easing — {previous.Deaths} death{(previous.Deaths == 1 ? "" : "s")}"
                : confidence > 0.7f
                    ? $"raising — cleared it comfortably (confidence {confidence:F2})"
                    : $"steady — confidence {confidence:F2}";
        }

        _difficulty = Mathf.Clamp(_difficulty + Mathf.Clamp(delta, -MaxFall, MaxRise), 0.05f, 1.0f);
    }

    private void UpdateMechanicTrust(SectionPerformance previous)
    {
        // Movers: dying on one is specific, actionable evidence. Pull it back
        // hard, then reintroduce gradually rather than never.
        if (previous.MoversPresent > 0)
        {
            _moverTrust = previous.MoverDeaths > 0
                ? _moverTrust * 0.40f
                : Grow(_moverTrust, 0.14f);
        }
        else
        {
            // Absent mechanics recover slowly on their own, so a single bad
            // section doesn't remove them from the game permanently.
            _moverTrust = Grow(_moverTrust, 0.04f);
        }

        if (previous.CrumblesPresent > 0)
        {
            _crumbleTrust = previous.CrumbleDeaths > 0
                ? _crumbleTrust * 0.45f
                : Grow(_crumbleTrust, 0.14f);
        }
        else
        {
            _crumbleTrust = Grow(_crumbleTrust, 0.04f);
        }

        // Bounce pads are measured by use, not death: a player who walks past
        // them has not understood them, which is its own kind of failure.
        if (previous.BouncePadsPresent > 0)
        {
            float used = (float)previous.BouncePadsUsed / previous.BouncePadsPresent;
            _bounceTrust = used > 0.6f ? Grow(_bounceTrust, 0.16f) : _bounceTrust * 0.75f;
        }
        else
        {
            _bounceTrust = Grow(_bounceTrust, 0.05f);
        }
    }

    private void UpdateShardRisk(SectionPerformance previous)
    {
        // Collecting everything means the optional route was not optional
        // enough; missing most of them means it was already too much.
        if (previous.ShardRatio > 0.85f && !previous.Struggled)
            _shardRisk = Mathf.Min(_shardRisk + 0.15f, 1.0f);
        else if (previous.ShardRatio < 0.40f)
            _shardRisk = Mathf.Max(_shardRisk - 0.20f, 0.0f);
    }

    private static float Grow(float value, float amount) => Mathf.Min(value + amount, 1.0f);

    // -----------------------------------------------------------------------
    // Building the recipe
    // -----------------------------------------------------------------------

    private SectionSpec Build(SectionPerformance previous)
    {
        float d = _difficulty;

        // Gaps scale toward the envelope's safe maximum, never past it. This is
        // the invariant that keeps generated courses possible: the clamp is on
        // the envelope, not on a number someone typed.
        float gapCeiling = _envelope.SafeGap;
        float gapMax = Mathf.Lerp(2.2f, gapCeiling, d);

        // Hesitation means they are struggling to read distances, so narrow the
        // spread: consistent gaps are learnable, varied ones are guesswork.
        float spread = previous.HesitationRatio > 0.25f ? 0.45f : 0.75f;
        float gapMin = Mathf.Max(1.1f, gapMax * (1.0f - spread * 0.6f));

        float riseMax = Mathf.Lerp(0.8f, _envelope.SafeRise, d);

        // Platforms shrink as difficulty climbs, but never below somewhere the
        // player can actually stand and turn around.
        float platformSize = Mathf.Lerp(6.0f, 3.0f, d);

        return new SectionSpec
        {
            Difficulty = d,
            PlatformCount = Mathf.RoundToInt(Mathf.Lerp(5, 9, d)),

            GapMin = gapMin,
            GapMax = gapMax,
            RiseMin = -Mathf.Lerp(0.6f, 2.0f, d),
            RiseMax = riseMax,
            PlatformSize = platformSize,

            // Hazards are pure pressure with no new skill to learn, so they
            // arrive later than the mechanics do.
            HazardChance = Mathf.Clamp(d * 0.75f - 0.1f, 0f, 0.6f),

            // Each mechanic's frequency is gated by its own trust, so the game
            // can be hard in general while going easy on the one thing this
            // player keeps dying to.
            MoverChance = 0.35f * _moverTrust * Mathf.Clamp(d * 1.6f, 0f, 1f),
            CrumbleChance = 0.30f * _crumbleTrust * Mathf.Clamp(d * 1.4f, 0f, 1f),
            BounceChance = 0.28f * _bounceTrust,

            TurnChance = Mathf.Lerp(0.15f, 0.38f, d),

            ShardCount = 2 + (_shardRisk > 0.5f ? 1 : 0),
            ShardRisk = _shardRisk,

            Seed = _rng.Randi(),
        };
    }

    /// <summary>A short description of the director's state, for the HUD.</summary>
    public string Describe() =>
        $"difficulty {_difficulty:F2}  ·  movers {_moverTrust:F2}  ·  " +
        $"crumble {_crumbleTrust:F2}  ·  bounce {_bounceTrust:F2}";
}
