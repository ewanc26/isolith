using Godot;

namespace Isolith.Level.Generation;

/// <summary>
/// The recipe for one generated section. Produced by
/// <see cref="AdaptiveDirector"/>, consumed by <see cref="SectionGenerator"/>.
/// </summary>
/// <remarks>
/// Separating the recipe from the geometry keeps the reactive logic testable
/// without building any nodes, and keeps the generator a pure function of
/// (spec, seed, entry point).
/// </remarks>
public sealed class SectionSpec
{
    /// <summary>Overall difficulty this section was built for, 0–1. For display and tests.</summary>
    public float Difficulty { get; init; }

    /// <summary>Platforms in the section, excluding the entry platform.</summary>
    public int PlatformCount { get; init; } = 6;

    /// <summary>Smallest edge-to-edge gap, in metres.</summary>
    public float GapMin { get; init; } = 1.5f;

    /// <summary>Largest edge-to-edge gap. Never exceeds <see cref="JumpEnvelope.SafeGap"/>.</summary>
    public float GapMax { get; init; } = 3.0f;

    /// <summary>Most a platform may drop relative to the previous one.</summary>
    public float RiseMin { get; init; } = -0.8f;

    /// <summary>Most a platform may climb. Never exceeds <see cref="JumpEnvelope.SafeRise"/>.</summary>
    public float RiseMax { get; init; } = 1.0f;

    /// <summary>Nominal platform footprint. Smaller means less margin for error.</summary>
    public float PlatformSize { get; init; } = 5.0f;

    /// <summary>Chance a gap is floored with a spike volume, 0–1.</summary>
    public float HazardChance { get; init; }

    /// <summary>Chance a platform is a crumbling one, 0–1.</summary>
    public float CrumbleChance { get; init; }

    /// <summary>Chance a platform is a bounce pad, 0–1.</summary>
    public float BounceChance { get; init; }

    /// <summary>Chance a platform is replaced by a moving one, 0–1.</summary>
    public float MoverChance { get; init; }

    /// <summary>Chance the path turns 90° at a given platform, 0–1.</summary>
    public float TurnChance { get; init; } = 0.2f;

    /// <summary>Collectibles placed in the section.</summary>
    public int ShardCount { get; init; } = 2;

    /// <summary>
    /// How far shards stray from the safe path, 0–1. At 0 they sit on platforms;
    /// at 1 they hang over gaps and demand a detour.
    /// </summary>
    public float ShardRisk { get; init; }

    /// <summary>Deterministic seed for this section's geometry.</summary>
    public ulong Seed { get; init; }

    /// <summary>
    /// The opening section: gentle, wide, and free of every mechanic, so the
    /// first thing a player meets is plain jumping.
    /// </summary>
    public static SectionSpec Opening(ulong seed) => new()
    {
        Difficulty = 0.15f,
        PlatformCount = 5,
        GapMin = 1.2f,
        GapMax = 2.4f,
        RiseMin = -0.4f,
        RiseMax = 0.7f,
        PlatformSize = 6.0f,
        HazardChance = 0f,
        CrumbleChance = 0f,
        BounceChance = 0f,
        MoverChance = 0f,
        TurnChance = 0.15f,
        ShardCount = 2,
        ShardRisk = 0f,
        Seed = seed,
    };

    public override string ToString() =>
        $"d={Difficulty:F2} n={PlatformCount} gap={GapMin:F1}-{GapMax:F1} " +
        $"rise={RiseMin:F1}-{RiseMax:F1} size={PlatformSize:F1} " +
        $"haz={HazardChance:F2} cru={CrumbleChance:F2} bou={BounceChance:F2} mov={MoverChance:F2} " +
        $"shards={ShardCount}@{ShardRisk:F2}";
}
