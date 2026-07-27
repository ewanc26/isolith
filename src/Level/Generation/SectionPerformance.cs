using Godot;

namespace Isolith.Level.Generation;

/// <summary>
/// What the player actually did in one section. This is the input the generator
/// reacts to — everything the director knows about the player comes from here.
/// </summary>
/// <remarks>
/// Deliberately more than "did they die": deaths alone cannot tell a player who
/// is cruising apart from one who is clearing every gap by a hair's breadth and
/// about to fall apart. The near-miss and hesitation signals catch that before
/// the deaths arrive.
/// </remarks>
public sealed class SectionPerformance
{
    /// <summary>Index of the section this describes.</summary>
    public int SectionIndex { get; init; }

    /// <summary>Seconds from entering the section to leaving it.</summary>
    public float Duration { get; set; }

    /// <summary>How long the section "should" take, from its length and the player's speed.</summary>
    public float ExpectedDuration { get; set; } = 1.0f;

    public int Deaths { get; set; }
    public int Jumps { get; set; }

    public int ShardsPresent { get; set; }
    public int ShardsCollected { get; set; }

    /// <summary>Landings that touched down within a stride of a platform edge.</summary>
    public int EdgeLandings { get; set; }

    /// <summary>Landings in total, so <see cref="EdgeLandings"/> can be a ratio.</summary>
    public int Landings { get; set; }

    /// <summary>Seconds spent grounded and not moving — hesitation before a jump.</summary>
    public float IdleSeconds { get; set; }

    // --- Per-mechanic outcomes ----------------------------------------------

    public int MoversPresent { get; set; }
    public int MoverDeaths { get; set; }

    public int BouncePadsPresent { get; set; }
    public int BouncePadsUsed { get; set; }

    public int CrumblesPresent { get; set; }
    public int CrumbleDeaths { get; set; }

    // --- Derived signals -----------------------------------------------------

    /// <summary>How much slower than expected the section was. 1.0 is on pace.</summary>
    public float Pace => ExpectedDuration <= 0.01f
        ? 1.0f
        : Mathf.Clamp(Duration / ExpectedDuration, 0.2f, 5.0f);

    /// <summary>Fraction of landings that were close to an edge, 0–1.</summary>
    public float EdgeRatio => Landings == 0 ? 0f : (float)EdgeLandings / Landings;

    /// <summary>Fraction of the section's shards collected, 0–1.</summary>
    public float ShardRatio => ShardsPresent == 0 ? 1f : (float)ShardsCollected / ShardsPresent;

    /// <summary>Fraction of the section's duration spent standing still, 0–1.</summary>
    public float HesitationRatio => Duration <= 0.01f ? 0f : Mathf.Clamp(IdleSeconds / Duration, 0f, 1f);

    /// <summary>
    /// A single 0–1 read on how comfortably the section went. 0.5 is "about
    /// right"; below 0.35 is struggling, above 0.7 is cruising.
    /// </summary>
    public float Confidence
    {
        get
        {
            // Deaths dominate — nothing else outweighs actually failing.
            float score = 1.0f - Mathf.Min(Deaths * 0.28f, 0.85f);

            // Clearing gaps by a hair is a warning even without deaths.
            score -= EdgeRatio * 0.20f;

            // Standing at the lip working up to a jump is a different kind of
            // struggle, and one that never shows up as a death.
            score -= HesitationRatio * 0.25f;

            // Slower than expected suggests caution; faster suggests command.
            score -= Mathf.Clamp((Pace - 1.0f) * 0.18f, -0.15f, 0.30f);

            // Detouring for optional shards is something only a comfortable
            // player bothers to do.
            score += (ShardRatio - 0.5f) * 0.12f;

            return Mathf.Clamp(score, 0f, 1f);
        }
    }

    /// <summary>True when the section clearly went badly.</summary>
    public bool Struggled => Deaths > 0 || Confidence < 0.35f;

    /// <summary>True when the section was comfortably within the player's ability.</summary>
    public bool Cruised => Deaths == 0 && Confidence > 0.70f;

    public override string ToString() =>
        $"#{SectionIndex} conf={Confidence:F2} deaths={Deaths} edge={EdgeRatio:P0} " +
        $"pace={Pace:F2} shards={ShardsCollected}/{ShardsPresent}";
}
