using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace Isolith.Level.Generation;

/// <summary>Where a section hands over to the next one.</summary>
/// <param name="Surface">Top-centre of the final platform.</param>
/// <param name="Direction">Unit heading on the XZ plane.</param>
/// <param name="HalfExtent">Half the final platform's footprint along <paramref name="Direction"/>.</param>
public readonly record struct SectionAnchor(Vector3 Surface, Vector3 Direction, float HalfExtent);

/// <summary>
/// One jump the generated path asks the player to make. Recorded so the
/// traversability invariant can be asserted directly rather than inferred back
/// out of block positions.
/// </summary>
/// <param name="Gap">Edge-to-edge horizontal distance.</param>
/// <param name="Rise">Height change; negative is a drop.</param>
/// <param name="AfterBounce">True when launched from a bounce pad, which widens the envelope.</param>
/// <param name="IsMover">True when the landing is a moving platform.</param>
public readonly record struct PathStep(float Gap, float Rise, bool AfterBounce, bool IsMover);

/// <summary>One generated section: its geometry, and how it connects onward.</summary>
public sealed class GeneratedSection
{
    public required int Index { get; init; }
    public required SectionSpec Spec { get; init; }
    public required Course Course { get; init; }
    public required SectionAnchor Exit { get; init; }

    /// <summary>Roughly how long a competent player should need, in seconds.</summary>
    public required float ExpectedDuration { get; init; }

    public required int MoverCount { get; init; }
    public required int BounceCount { get; init; }
    public required int CrumbleCount { get; init; }
    public required int ShardCount { get; init; }

    /// <summary>Every jump in the section, in order.</summary>
    public required IReadOnlyList<PathStep> Path { get; init; }
}

/// <summary>
/// Turns a <see cref="SectionSpec"/> into geometry. Pure and deterministic: the
/// same spec, seed, and entry anchor always produce the same section.
/// </summary>
/// <remarks>
/// The generator emits a <see cref="Course"/> through the same JSON path that
/// authored levels use. That is not a detour — it means generated content is
/// validated, hashed, and buildable by exactly the same code as hand-made
/// content, and any section can be dumped to a file and replayed as a fixed
/// course when something goes wrong.
///
/// <b>The invariant:</b> every gap this produces is clamped to
/// <see cref="JumpEnvelope"/>. The clamp is against the player's real tuning,
/// not a magic number, so a generated course cannot become impossible by
/// someone retuning the character.
/// </remarks>
public sealed class SectionGenerator
{
    private const float Thickness = 1.0f;
    private const float MoverThickness = 0.6f;

    /// <summary>How far a moving platform travels, as a fraction of the gap budget.</summary>
    private const float MoverTravelMin = 3.0f;
    private const float MoverTravelMax = 6.5f;

    private readonly JumpEnvelope _envelope;

    public SectionGenerator(JumpEnvelope envelope) => _envelope = envelope;

    /// <summary>Generates a section starting from <paramref name="entry"/>.</summary>
    public GeneratedSection Generate(int index, SectionSpec spec, SectionAnchor entry)
    {
        var rng = new RandomNumberGenerator { Seed = spec.Seed };

        var blocks = new List<BlockDef>();
        var movers = new List<MoverDef>();
        var shards = new List<float[]>();
        var checkpoints = new List<float[]>();

        Vector3 surface = entry.Surface;
        Vector3 direction = entry.Direction;
        float previousHalf = entry.HalfExtent;

        int moverCount = 0, bounceCount = 0, crumbleCount = 0;
        float pathLength = 0f;
        var path = new List<PathStep>();

        // Landing spots, recorded as we go, so shards can be placed against the
        // route that actually exists rather than guessed at afterwards.
        var landings = new List<(Vector3 Surface, Vector3 Direction, float Gap, Vector3 Previous)>();

        // Whether the previous platform launches the player, which widens what
        // the next gap is allowed to be.
        bool launchedFromBounce = false;

        for (int step = 0; step < spec.PlatformCount; step++)
        {
            if (step > 0 && rng.Randf() < spec.TurnChance)
                direction = Turn(direction, rng.Randf() < 0.5f);

            float size = spec.PlatformSize * rng.RandfRange(0.85f, 1.15f);
            float half = size * 0.5f;

            // The reach available for this jump. A bounce pad behind us buys a
            // much larger envelope; everything is clamped to it either way.
            float gapCeiling = launchedFromBounce ? _envelope.SafeBounceGap : _envelope.SafeGap;
            float riseCeiling = launchedFromBounce ? _envelope.SafeBounceRise : _envelope.SafeRise;

            float gap = Mathf.Min(rng.RandfRange(spec.GapMin, spec.GapMax), gapCeiling);
            float rise = Mathf.Clamp(rng.RandfRange(spec.RiseMin, spec.RiseMax), -6.0f, riseCeiling);

            // A bounce pad is wasted unless the next platform is worth the
            // height, so spend some of the extra envelope on going up.
            if (launchedFromBounce)
                rise = Mathf.Max(rise, riseCeiling * 0.55f);

            BlockKind kind = ChooseKind(spec, rng, step, launchedFromBounce);

            bool isMover = kind == BlockKind.Solid
                           && step > 0
                           && step < spec.PlatformCount - 1
                           && rng.Randf() < spec.MoverChance;

            Vector3 previousSurface = surface;
            float advance = previousHalf + gap + half;
            Vector3 landing = surface + direction * advance + Vector3.Up * rise;

            if (isMover)
            {
                // The platform travels along the direction of travel, so the
                // player boards it at the near end and leaves from the far end.
                // Both ends are a normal gap from their neighbour, which is what
                // makes a moving platform safe to generate: waiting always works.
                float travel = rng.RandfRange(MoverTravelMin, MoverTravelMax);
                Vector3 from = landing - Vector3.Up * (MoverThickness * 0.5f);
                Vector3 to = from + direction * travel;

                movers.Add(new MoverDef
                {
                    From = Components(from),
                    To = Components(to),
                    Size = new[] { size, MoverThickness, size },
                    Period = rng.RandfRange(4.0f, 7.0f),
                    Phase = rng.Randf(),
                });

                moverCount++;
                surface = to + Vector3.Up * (MoverThickness * 0.5f);
                pathLength += advance + travel;
            }
            else
            {
                blocks.Add(new BlockDef
                {
                    Position = Components(landing - Vector3.Up * (Thickness * 0.5f)),
                    Size = new[] { size, Thickness, size },
                    Kind = kind,
                });

                if (kind == BlockKind.Bounce) bounceCount++;
                if (kind == BlockKind.Crumble) crumbleCount++;

                surface = landing;
                pathLength += advance;
            }

            path.Add(new PathStep(gap, rise, launchedFromBounce, isMover));
            landings.Add((surface, direction, gap, previousSurface));

            // Spike the gap we just crossed. Purely pressure: falling was
            // already fatal, but seeing what is under the jump changes how it
            // feels to commit to it.
            if (gap > 2.0f && rng.Randf() < spec.HazardChance)
                blocks.Add(Hazard(previousSurface, surface, gap));

            launchedFromBounce = kind == BlockKind.Bounce;
            previousHalf = half;

            // A checkpoint early in the section, so a death costs one section
            // at most.
            if (step == 0)
                checkpoints.Add(Components(surface + Vector3.Up * 0.1f));
        }

        PlaceShards(spec, rng, landings, shards);

        Course course = Materialise(spec, blocks, movers, shards, checkpoints, entry);

        // Travel time plus a little per jump for the arc and the hesitation
        // before it. Only used as a pace baseline, so approximate is fine.
        float expected = pathLength / 7.0f + spec.PlatformCount * 0.45f;

        return new GeneratedSection
        {
            Index = index,
            Spec = spec,
            Course = course,
            Exit = new SectionAnchor(surface, direction, previousHalf),
            ExpectedDuration = Mathf.Max(expected, 1.0f),
            MoverCount = moverCount,
            BounceCount = bounceCount,
            CrumbleCount = crumbleCount,
            ShardCount = shards.Count,
            Path = path,
        };
    }

    // -----------------------------------------------------------------------
    // Pieces
    // -----------------------------------------------------------------------

    private static BlockKind ChooseKind(SectionSpec spec, RandomNumberGenerator rng, int step, bool afterBounce)
    {
        // Never two bounce pads in a row: the second one is unreadable while
        // still airborne from the first.
        if (!afterBounce && step > 0 && rng.Randf() < spec.BounceChance)
            return BlockKind.Bounce;

        // Crumbling platforms are never the section's first landing — arriving
        // from the previous section onto ground that is already failing gives
        // the player no chance to read it.
        if (step > 0 && rng.Randf() < spec.CrumbleChance)
            return BlockKind.Crumble;

        return rng.Randf() < 0.30f ? BlockKind.Grass : BlockKind.Solid;
    }

    private static BlockDef Hazard(Vector3 from, Vector3 to, float gap)
    {
        Vector3 midpoint = (from + to) * 0.5f;
        float lowest = Mathf.Min(from.Y, to.Y);

        return new BlockDef
        {
            // Sunk below the platforms so it reads as a pit, not a wall.
            Position = new[] { midpoint.X, lowest - 1.8f, midpoint.Z },
            Size = new[] { gap + 2.0f, 2.5f, gap + 2.0f },
            Kind = BlockKind.Hazard,
        };
    }

    private static void PlaceShards(
        SectionSpec spec,
        RandomNumberGenerator rng,
        List<(Vector3 Surface, Vector3 Direction, float Gap, Vector3 Previous)> landings,
        List<float[]> shards)
    {
        if (landings.Count == 0)
            return;

        for (int i = 0; i < spec.ShardCount; i++)
        {
            var landing = landings[rng.RandiRange(0, landings.Count - 1)];

            if (rng.Randf() < spec.ShardRisk)
            {
                // Over the gap: collectable only by shaping the jump around it,
                // which is the point. Height stays inside the arc a normal jump
                // already traces, so it is a detour, not a separate challenge.
                Vector3 midpoint = (landing.Previous + landing.Surface) * 0.5f;
                shards.Add(Components(midpoint + Vector3.Up * rng.RandfRange(1.1f, 1.8f)));
            }
            else
            {
                // On the route: a reward for looking up, not for risk.
                shards.Add(Components(landing.Surface + Vector3.Up * rng.RandfRange(1.2f, 2.0f)));
            }
        }
    }

    /// <summary>
    /// Serialises the generated pieces and parses them back as a
    /// <see cref="Course"/>, so generated content takes the same validated,
    /// hashable path as an authored level.
    /// </summary>
    private static Course Materialise(
        SectionSpec spec,
        List<BlockDef> blocks,
        List<MoverDef> movers,
        List<float[]> shards,
        List<float[]> checkpoints,
        SectionAnchor entry)
    {
        var course = new Course
        {
            Id = $"generated-{spec.Seed}",
            Name = $"Generated (difficulty {spec.Difficulty:F2})",
            Author = "AdaptiveDirector",
            Spawn = Components(entry.Surface + Vector3.Up * 1.0f),

            // Sections stream past; the global kill plane is owned by the
            // endless course, which knows where the player actually is.
            KillPlaneY = -10_000f,

            Blocks = blocks,
            Movers = movers,
            Shards = shards,
            Checkpoints = checkpoints,
            Goal = Components(entry.Surface),
        };

        return Course.Parse(JsonSerializer.Serialize(course), $"generated:{spec.Seed}");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static float[] Components(Vector3 value) => new[] { value.X, value.Y, value.Z };

    /// <summary>Rotates a unit XZ heading by 90°.</summary>
    private static Vector3 Turn(Vector3 direction, bool clockwise) =>
        clockwise
            ? new Vector3(direction.Z, 0, -direction.X)
            : new Vector3(-direction.Z, 0, direction.X);
}
