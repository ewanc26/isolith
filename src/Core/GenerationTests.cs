using System;
using System.Collections.Generic;
using Godot;
using Isolith.Level;
using Isolith.Level.Generation;

namespace Isolith.Core;

/// <summary>
/// Checks on procedural generation. Pure logic — no scene, no physics — so
/// thousands of sections can be exercised in well under a second.
/// </summary>
/// <remarks>
/// The one that matters is <see cref="TraversabilityHolds"/>. Everything else in
/// the generator is taste; a section the player physically cannot cross is a
/// broken game, and random content means it would surface as a rare, hard to
/// reproduce complaint rather than an obvious bug. So it is asserted directly,
/// over the full difficulty range and an adversarial director, on every run.
/// </remarks>
public static class GenerationTests
{
    /// <summary>Runs every generation check, reporting through <paramref name="check"/>.</summary>
    public static void Run(Action<string, bool> check)
    {
        JumpEnvelope envelope = JumpEnvelope.Default();

        EnvelopeIsSane(envelope, check);
        TraversabilityHolds(envelope, check);
        GenerationIsDeterministic(envelope, check);
        SectionsConnect(envelope, check);
        DirectorEasesAfterDeaths(envelope, check);
        DirectorRaisesAfterCleanRuns(envelope, check);
        DirectorTargetsTheGuiltyMechanic(envelope, check);
        DirectorStaysInBounds(envelope, check);
        DirectorHoldsOnEdgeLandings(envelope, check);
    }

    // -----------------------------------------------------------------------
    // Envelope
    // -----------------------------------------------------------------------

    private static void EnvelopeIsSane(JumpEnvelope envelope, Action<string, bool> check)
    {
        check($"envelope derived from player tuning ({envelope})",
            envelope.MaxRange > 4.0f && envelope.MaxRange < 10.0f);

        check("safe gap leaves a margin under the true reach",
            envelope.SafeGap < envelope.MaxRange);

        check("a bounce pad reaches further than a jump",
            envelope.SafeBounceGap > envelope.SafeGap);
    }

    // -----------------------------------------------------------------------
    // The invariant
    // -----------------------------------------------------------------------

    /// <summary>
    /// No generated jump may exceed what the player can physically do, at any
    /// difficulty, for any seed.
    /// </summary>
    private static void TraversabilityHolds(JumpEnvelope envelope, Action<string, bool> check)
    {
        var generator = new SectionGenerator(envelope);

        int steps = 0;
        float worstGapRatio = 0f;
        float worstRiseRatio = 0f;
        var failures = new List<string>();

        // Sweep the whole difficulty range rather than only what the director
        // would realistically produce: a future tuning change must not be able
        // to walk generation into the impossible.
        for (int trial = 0; trial < 400; trial++)
        {
            float difficulty = (trial % 21) / 20.0f;
            SectionSpec spec = Adversarial(difficulty, seed: (ulong)(trial * 7919 + 13));

            var entry = new SectionAnchor(Vector3.Zero, Vector3.Forward, 4.0f);
            GeneratedSection section = generator.Generate(trial, spec, entry);

            foreach (PathStep step in section.Path)
            {
                steps++;

                float gapBudget = step.AfterBounce ? envelope.SafeBounceGap : envelope.SafeGap;
                float riseBudget = step.AfterBounce ? envelope.SafeBounceRise : envelope.SafeRise;

                worstGapRatio = Mathf.Max(worstGapRatio, step.Gap / gapBudget);
                worstRiseRatio = Mathf.Max(worstRiseRatio, step.Rise / riseBudget);

                // A hair over is floating-point noise, not a design failure.
                if (step.Gap > gapBudget + 0.001f)
                    failures.Add($"gap {step.Gap:F2} > {gapBudget:F2} at difficulty {difficulty:F2}");

                if (step.Rise > riseBudget + 0.001f)
                    failures.Add($"rise {step.Rise:F2} > {riseBudget:F2} at difficulty {difficulty:F2}");
            }
        }

        check($"every generated jump is within reach ({steps} jumps, worst gap {worstGapRatio:P0} " +
              $"of budget, worst rise {worstRiseRatio:P0})",
            failures.Count == 0);

        foreach (string failure in failures.GetRange(0, Math.Min(3, failures.Count)))
            GD.PrintErr($"        {failure}");

        // A generator that clamps everything to a trivial fraction is "safe" and
        // useless; the hard cases should actually approach the budget.
        check($"difficulty reaches the budget it is allowed ({worstGapRatio:P0})",
            worstGapRatio > 0.9f);
    }

    private static void SectionsConnect(JumpEnvelope envelope, Action<string, bool> check)
    {
        var generator = new SectionGenerator(envelope);
        var entry = new SectionAnchor(Vector3.Zero, Vector3.Forward, 4.0f);

        bool moved = true;
        bool grounded = true;
        SectionAnchor anchor = entry;

        for (int i = 0; i < 40; i++)
        {
            GeneratedSection section = generator.Generate(i, Adversarial(0.6f, (ulong)(i + 1) * 104729), anchor);

            if (section.Exit.Surface.IsEqualApprox(anchor.Surface))
                moved = false;

            if (!float.IsFinite(section.Exit.Surface.Y) || section.Exit.HalfExtent <= 0.5f)
                grounded = false;

            anchor = section.Exit;
        }

        check("each section advances the frontier", moved);
        check("exit anchors stay finite and standable", grounded);
    }

    private static void GenerationIsDeterministic(JumpEnvelope envelope, Action<string, bool> check)
    {
        var generator = new SectionGenerator(envelope);
        var entry = new SectionAnchor(Vector3.Zero, Vector3.Forward, 4.0f);
        SectionSpec spec = Adversarial(0.55f, seed: 424242);

        GeneratedSection first = generator.Generate(0, spec, entry);
        GeneratedSection second = generator.Generate(0, spec, entry);

        // Same seed, same course JSON: this is what makes a generated run
        // reproducible from its seed alone, and therefore reportable.
        check("the same seed produces the same section",
            first.Course.Hash == second.Course.Hash);
    }

    // -----------------------------------------------------------------------
    // The director
    // -----------------------------------------------------------------------

    private static void DirectorEasesAfterDeaths(JumpEnvelope envelope, Action<string, bool> check)
    {
        var director = new AdaptiveDirector(envelope, seed: 1);
        director.Next(null);

        // Settle at a workable difficulty first, so there is room to fall.
        for (int i = 0; i < 6; i++)
            director.Next(Cruised(i));

        float before = director.Difficulty;
        director.Next(Died(99, deaths: 3));
        float after = director.Difficulty;

        check($"dying eases the next section ({before:F2} → {after:F2})", after < before);
    }

    private static void DirectorRaisesAfterCleanRuns(JumpEnvelope envelope, Action<string, bool> check)
    {
        var director = new AdaptiveDirector(envelope, seed: 2);
        director.Next(null);

        float before = director.Difficulty;
        for (int i = 0; i < 8; i++)
            director.Next(Cruised(i));

        check($"cruising raises difficulty ({before:F2} → {director.Difficulty:F2})",
            director.Difficulty > before);

        // Asymmetry: relief must arrive faster than pressure does.
        var faller = new AdaptiveDirector(envelope, seed: 2);
        faller.Next(null);
        for (int i = 0; i < 8; i++)
            faller.Next(Cruised(i));

        float peak = faller.Difficulty;
        faller.Next(Died(9, deaths: 2));
        float drop = peak - faller.Difficulty;
        float rise = (director.Difficulty - before) / 8f;

        check($"one bad section relieves more than one good section adds ({drop:F3} vs {rise:F3})",
            drop > rise);
    }

    private static void DirectorTargetsTheGuiltyMechanic(JumpEnvelope envelope, Action<string, bool> check)
    {
        var director = new AdaptiveDirector(envelope, seed: 3);
        director.Next(null);

        for (int i = 0; i < 6; i++)
            director.Next(Cruised(i, movers: 2, crumbles: 2));

        float moversBefore = director.MoverTrust;
        float crumbleBefore = director.CrumbleTrust;

        // Died specifically on a moving platform.
        SectionPerformance sample = Died(7, deaths: 2);
        sample.MoversPresent = 2;
        sample.MoverDeaths = 2;
        sample.CrumblesPresent = 2;
        sample.CrumbleDeaths = 0;

        SectionSpec next = director.Next(sample);

        check($"mover deaths cut mover trust ({moversBefore:F2} → {director.MoverTrust:F2})",
            director.MoverTrust < moversBefore * 0.6f);

        check($"crumble trust is untouched by a mover death " +
              $"({crumbleBefore:F2} → {director.CrumbleTrust:F2})",
            director.CrumbleTrust >= crumbleBefore);

        check($"the next section backs off movers (chance {next.MoverChance:F3})",
            next.MoverChance < 0.12f);
    }

    private static void DirectorHoldsOnEdgeLandings(JumpEnvelope envelope, Action<string, bool> check)
    {
        var director = new AdaptiveDirector(envelope, seed: 4);
        director.Next(null);

        for (int i = 0; i < 5; i++)
            director.Next(Cruised(i));

        float before = director.Difficulty;

        // No deaths, but only just making every jump. Should not escalate.
        SectionPerformance sample = Cruised(6);
        sample.Landings = 10;
        sample.EdgeLandings = 8;

        director.Next(sample);

        check($"near-miss landings stop escalation ({before:F2} → {director.Difficulty:F2})",
            director.Difficulty <= before + 0.0001f);
    }

    private static void DirectorStaysInBounds(JumpEnvelope envelope, Action<string, bool> check)
    {
        var director = new AdaptiveDirector(envelope, seed: 5);
        director.Next(null);

        bool inRange = true;
        var rng = new RandomNumberGenerator { Seed = 99 };

        // Random walk through wildly varying performances, including nonsense
        // ones, to make sure nothing runs away or goes negative.
        for (int i = 0; i < 500; i++)
        {
            var sample = new SectionPerformance
            {
                SectionIndex = i,
                Duration = rng.RandfRange(0.1f, 60f),
                ExpectedDuration = rng.RandfRange(1f, 20f),
                Deaths = rng.RandiRange(0, 12),
                Landings = rng.RandiRange(0, 30),
                EdgeLandings = rng.RandiRange(0, 30),
                IdleSeconds = rng.RandfRange(0f, 30f),
                ShardsPresent = rng.RandiRange(0, 5),
                ShardsCollected = rng.RandiRange(0, 5),
                MoversPresent = rng.RandiRange(0, 3),
                MoverDeaths = rng.RandiRange(0, 3),
                CrumblesPresent = rng.RandiRange(0, 3),
                CrumbleDeaths = rng.RandiRange(0, 3),
                BouncePadsPresent = rng.RandiRange(0, 3),
                BouncePadsUsed = rng.RandiRange(0, 3),
            };

            SectionSpec spec = director.Next(sample);

            if (director.Difficulty is < 0f or > 1f)
                inRange = false;

            if (spec.GapMax > envelope.SafeGap + 0.001f || spec.PlatformCount < 1)
                inRange = false;

            if (spec.MoverChance is < 0f or > 1f || spec.HazardChance is < 0f or > 1f)
                inRange = false;
        }

        check("the director stays in bounds under 500 random sections", inRange);
    }

    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    /// <summary>
    /// A spec that pushes every parameter past what the director would ask for,
    /// so the generator's own clamping is what is under test.
    /// </summary>
    private static SectionSpec Adversarial(float difficulty, ulong seed) => new()
    {
        Difficulty = difficulty,
        PlatformCount = 8,

        // Deliberately beyond the envelope: if the generator does not clamp
        // these, the traversability check fails.
        GapMin = 1.0f + difficulty * 6.0f,
        GapMax = 3.0f + difficulty * 20.0f,
        RiseMin = -4.0f,
        RiseMax = 1.0f + difficulty * 12.0f,

        PlatformSize = Mathf.Lerp(6.0f, 3.0f, difficulty),
        HazardChance = difficulty * 0.6f,
        CrumbleChance = difficulty * 0.4f,
        BounceChance = difficulty * 0.35f,
        MoverChance = difficulty * 0.4f,
        TurnChance = 0.3f,
        ShardCount = 3,
        ShardRisk = difficulty,
        Seed = seed,
    };

    private static SectionPerformance Cruised(int index, int movers = 0, int crumbles = 0) => new()
    {
        SectionIndex = index,
        Duration = 8f,
        ExpectedDuration = 10f,
        Deaths = 0,
        Jumps = 8,
        Landings = 8,
        EdgeLandings = 0,
        IdleSeconds = 0.2f,
        ShardsPresent = 2,
        ShardsCollected = 2,
        MoversPresent = movers,
        CrumblesPresent = crumbles,
    };

    private static SectionPerformance Died(int index, int deaths) => new()
    {
        SectionIndex = index,
        Duration = 30f,
        ExpectedDuration = 10f,
        Deaths = deaths,
        Jumps = 14,
        Landings = 10,
        EdgeLandings = 6,
        IdleSeconds = 8f,
        ShardsPresent = 2,
        ShardsCollected = 0,
    };
}
