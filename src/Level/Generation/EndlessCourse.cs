using System;
using System.Collections.Generic;
using Godot;
using Isolith.Gameplay;

namespace Isolith.Level.Generation;

/// <summary>
/// Streams an endless run: generates sections ahead of the player, measures how
/// each one goes, feeds that to the <see cref="AdaptiveDirector"/>, and frees
/// sections once they are behind.
/// </summary>
/// <remarks>
/// A section is finished the moment the player triggers the <em>next</em>
/// section's checkpoint. That is what makes the loop honest: the reaction is to
/// a section the player has actually completed, not to a guess about where they
/// are, and the checkpoint already exists for respawn purposes.
/// </remarks>
[GlobalClass]
public partial class EndlessCourse : Node3D
{
    /// <summary>Sections kept built ahead of the player.</summary>
    private const int LookAhead = 2;

    /// <summary>Sections kept behind before being freed.</summary>
    private const int LookBehind = 1;

    /// <summary>A landing this close to a platform edge counts as a near miss.</summary>
    private const float EdgeMargin = 0.9f;

    /// <summary>Below the current section by this much is a fall.</summary>
    private const float FallDepth = 18.0f;

    /// <summary>Raised when the player completes a section, with the measured result.</summary>
    public event Action<SectionPerformance, SectionSpec>? SectionCompleted;

    /// <summary>Raised whenever a shard in any live section is collected.</summary>
    public event Action? ShardCollected;

    /// <summary>Shards that have existed in this run, for the run's totals.</summary>
    public int ShardsSpawned { get; private set; }

    /// <summary>Sections fully cleared this run.</summary>
    public int SectionsCleared { get; private set; }

    /// <summary>The director, exposed for the HUD and for tests.</summary>
    public AdaptiveDirector Director { get; private set; } = null!;

    /// <summary>Where the player respawns after a fall.</summary>
    public Vector3 RespawnPoint { get; private set; }

    /// <summary>Y below which the player counts as having fallen out of the world.</summary>
    public float KillPlaneY => RespawnPoint.Y - FallDepth;

    private sealed class LiveSection
    {
        public required GeneratedSection Generated { get; init; }
        public required CourseBuilder.Built Built { get; init; }
        public required SectionPerformance Performance { get; init; }
    }

    private readonly List<LiveSection> _sections = new();

    private PlayerController _player = null!;
    private SectionGenerator _generator = null!;
    private JumpEnvelope _envelope;
    private SectionAnchor _frontier;
    private int _nextIndex;
    private int _currentIndex = -1;

    /// <summary>Starts a fresh endless run.</summary>
    public void Begin(PlayerController player, ulong seed)
    {
        _player = player;
        _envelope = JumpEnvelope.From(player);
        _generator = new SectionGenerator(_envelope);
        Director = new AdaptiveDirector(_envelope, seed);

        Clear();

        _frontier = BuildSpawnPad();
        RespawnPoint = _frontier.Surface + Vector3.Up * 1.0f;

        _player.Jumped += OnJumped;
        _player.Landed += OnLanded;

        for (int i = 0; i < LookAhead + 1; i++)
            AppendSection();

        _currentIndex = 0;
    }

    public override void _ExitTree()
    {
        if (_player is not null)
        {
            _player.Jumped -= OnJumped;
            _player.Landed -= OnLanded;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_sections.Count == 0 || _currentIndex < 0)
            return;

        SectionPerformance current = Current.Performance;
        current.Duration += (float)delta;

        // Hesitation: grounded, and barely moving. Standing at the lip of a jump
        // working up to it is a struggle signal that never becomes a death.
        if (_player.IsOnFloor() && new Vector2(_player.Velocity.X, _player.Velocity.Z).Length() < 0.6f)
            current.IdleSeconds += (float)delta;
    }

    // -----------------------------------------------------------------------
    // Section lifecycle
    // -----------------------------------------------------------------------

    private LiveSection Current => _sections[Mathf.Clamp(_currentIndex, 0, _sections.Count - 1)];

    /// <summary>Generates, builds, and links the next section onto the frontier.</summary>
    private void AppendSection()
    {
        // The director reacts to the most recently *finished* section. While the
        // player is still working through one, look-ahead uses the last
        // completed result, which is exactly the brief: react to the previous
        // section.
        SectionPerformance? previous = LastCompleted();
        SectionSpec spec = Director.Next(previous);

        GeneratedSection generated = _generator.Generate(_nextIndex, spec, _frontier);
        CourseBuilder.Built built = CourseBuilder.Build(generated.Course, this, includeGoal: false);

        var performance = new SectionPerformance
        {
            SectionIndex = generated.Index,
            ExpectedDuration = generated.ExpectedDuration,
            ShardsPresent = generated.ShardCount,
            MoversPresent = generated.MoverCount,
            BouncePadsPresent = generated.BounceCount,
            CrumblesPresent = generated.CrumbleCount,
        };

        var section = new LiveSection
        {
            Generated = generated,
            Built = built,
            Performance = performance,
        };

        ShardsSpawned += built.Shards.Count;

        foreach (Shard shard in built.Shards)
        {
            shard.Collected += _ =>
            {
                performance.ShardsCollected++;
                ShardCollected?.Invoke();
            };
        }

        foreach (Checkpoint checkpoint in built.Checkpoints)
            checkpoint.Activated += _ => OnCheckpointReached(generated.Index, checkpoint);

        _sections.Add(section);
        _frontier = generated.Exit;
        _nextIndex++;
    }

    private void OnCheckpointReached(int sectionIndex, Checkpoint checkpoint)
    {
        RespawnPoint = checkpoint.RespawnPoint;

        // Reaching this section's checkpoint means the previous one is done.
        int finished = sectionIndex - 1;
        if (finished < 0 || finished < _currentIndex)
            return;

        LiveSection? completed = Find(finished);
        if (completed is null)
            return;

        _currentIndex = sectionIndex;
        SectionsCleared = Mathf.Max(SectionsCleared, sectionIndex);

        SectionCompleted?.Invoke(completed.Performance, completed.Generated.Spec);

        // Keep the buffer full, then drop what is safely behind.
        while (_nextIndex < sectionIndex + 1 + LookAhead)
            AppendSection();

        PruneBehind(sectionIndex);
    }

    private void PruneBehind(int currentIndex)
    {
        for (int i = _sections.Count - 1; i >= 0; i--)
        {
            if (_sections[i].Generated.Index >= currentIndex - LookBehind)
                continue;

            Node3D root = _sections[i].Built.Root;
            RemoveChild(root);
            root.QueueFree();
            _sections.RemoveAt(i);
        }
    }

    private LiveSection? Find(int index)
    {
        foreach (LiveSection section in _sections)
        {
            if (section.Generated.Index == index)
                return section;
        }

        return null;
    }

    private SectionPerformance? LastCompleted()
    {
        IReadOnlyList<SectionPerformance> history = Director.History;
        return history.Count == 0 ? null : history[^1];
    }

    // -----------------------------------------------------------------------
    // Measurement
    // -----------------------------------------------------------------------

    private void OnJumped(bool fromBouncePad)
    {
        if (_currentIndex < 0 || _sections.Count == 0)
            return;

        SectionPerformance current = Current.Performance;
        current.Jumps++;

        if (fromBouncePad)
            current.BouncePadsUsed++;
    }

    private void OnLanded(float impactSpeed)
    {
        if (_currentIndex < 0 || _sections.Count == 0)
            return;

        SectionPerformance current = Current.Performance;
        current.Landings++;

        if (LandedNearEdge())
            current.EdgeLandings++;
    }

    /// <summary>
    /// True when the player touched down close to the lip of whatever they
    /// landed on — a jump that only just made it.
    /// </summary>
    private bool LandedNearEdge()
    {
        if (_player.LastFloor is not { } floor)
            return false;

        if (!TryFootprint(floor, out Vector3 centre, out Vector2 halfExtents))
            return false;

        Vector3 position = _player.GlobalPosition;
        float insetX = halfExtents.X - Mathf.Abs(position.X - centre.X);
        float insetZ = halfExtents.Y - Mathf.Abs(position.Z - centre.Z);

        return Mathf.Min(insetX, insetZ) < EdgeMargin;
    }

    /// <summary>Reads a body's XZ footprint from its box collision shape.</summary>
    private static bool TryFootprint(Node3D body, out Vector3 centre, out Vector2 halfExtents)
    {
        centre = body.GlobalPosition;
        halfExtents = Vector2.Zero;

        foreach (Node child in body.GetChildren())
        {
            if (child is CollisionShape3D { Shape: BoxShape3D box } shape)
            {
                centre = shape.GlobalPosition;
                halfExtents = new Vector2(box.Size.X * 0.5f, box.Size.Z * 0.5f);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Records a death against the current section and attributes it to the
    /// mechanic responsible, so the director can respond to the specific
    /// failure rather than just making everything easier.
    /// </summary>
    public void ReportDeath()
    {
        if (_currentIndex < 0 || _sections.Count == 0)
            return;

        SectionPerformance current = Current.Performance;
        current.Deaths++;

        switch (_player.LastFloor)
        {
            case MovingPlatform:
                current.MoverDeaths++;
                break;

            case CrumblePlatform:
                current.CrumbleDeaths++;
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Setup
    // -----------------------------------------------------------------------

    /// <summary>A generous starting platform, so the run does not open mid-jump.</summary>
    private SectionAnchor BuildSpawnPad()
    {
        var pad = new StaticBody3D
        {
            Name = "SpawnPad",
            Position = new Vector3(0, -0.5f, 0),
            CollisionLayer = CourseBuilder.Mask(CourseBuilder.Layers.World),
            CollisionMask = 0,
        };

        var size = new Vector3(9, 1, 9);
        pad.AddChild(CourseBuilder.BoxShape(size));
        pad.AddChild(CourseBuilder.BoxMesh(size, Palette.Grass));
        AddChild(pad);

        return new SectionAnchor(Vector3.Zero, Vector3.Forward, 4.5f);
    }

    private void Clear()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        _sections.Clear();
        _nextIndex = 0;
        _currentIndex = -1;
        SectionsCleared = 0;
        ShardsSpawned = 0;
    }
}
