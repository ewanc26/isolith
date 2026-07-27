using System;
using System.Collections.Generic;
using Godot;
using Isolith.Core;
using Isolith.Level;

namespace Isolith.Gameplay;

/// <summary>What the game is currently doing.</summary>
public enum GameState
{
    Playing,
    Paused,
    Complete,
}

/// <summary>
/// Owns a play session: loads the course, wires the player and camera to it,
/// keeps the run's stats, and handles death, restart and completion.
/// </summary>
/// <remarks>
/// Expected children of the scene this script is attached to:
/// <c>Player</c> (<see cref="PlayerController"/>), <c>IsoCamera</c>
/// (<see cref="IsometricCamera"/>), and <c>CourseRoot</c> (<see cref="Node3D"/>).
/// </remarks>
[GlobalClass]
public partial class GameManager : Node3D
{
    /// <summary>The course to load on startup.</summary>
    [Export(PropertyHint.File, "*.json")]
    public string CoursePath { get; set; } = "res://courses/ascent.json";

    /// <summary>Raised whenever the timer, shard count, or death count changes.</summary>
    public event Action<RunStats>? StatsChanged;

    /// <summary>Raised on entering a new state.</summary>
    public event Action<GameState>? StateChanged;

    /// <summary>Raised once when a course is finished, after the run is saved locally.</summary>
    public event Action<RunStats>? RunCompleted;

    /// <summary>The course currently loaded.</summary>
    public Course? Course { get; private set; }

    /// <summary>Stats for the attempt in progress.</summary>
    public RunStats Stats { get; private set; } = new();

    public GameState State { get; private set; } = GameState.Playing;

    /// <summary>Shared sound player, also used by the UI.</summary>
    public Sfx Audio { get; private set; } = null!;

    /// <summary>The player character, for UI that needs to suppress its input.</summary>
    public PlayerController Player => _player;

    private PlayerController _player = null!;
    private IsometricCamera _camera = null!;
    private Node3D _courseRoot = null!;

    private CourseBuilder.Built? _built;
    private Vector3 _respawnPoint;
    private double _elapsedSeconds;
    private bool _dying;
    private bool _uiFocused;

    public override void _Ready()
    {
        GameInput.Configure();

        _player = RequireChild<PlayerController>("Player");
        _camera = RequireChild<IsometricCamera>("IsoCamera");
        _courseRoot = RequireChild<Node3D>("CourseRoot");

        Audio = new Sfx { Name = "Sfx" };
        AddChild(Audio);

        _player.Camera = _camera;
        _camera.Target = _player;

        _player.Jumped += OnJumped;
        _player.Landed += OnLanded;

        LoadCourse(CoursePath);
    }

    public override void _Process(double delta)
    {
        if (State == GameState.Playing)
        {
            _elapsedSeconds += delta;
            Stats.TimeMs = (int)(_elapsedSeconds * 1000.0);
            StatsChanged?.Invoke(Stats);

            CheckKillPlane();
        }

        // While a panel has keyboard focus its text fields own these keys.
        if (_uiFocused)
            return;

        if (Input.IsActionJustPressed(GameInput.Restart))
            Restart();
        else if (Input.IsActionJustPressed(GameInput.Pause))
            TogglePause();
    }

    // -----------------------------------------------------------------------
    // Course lifecycle
    // -----------------------------------------------------------------------

    /// <summary>Loads and builds a course, replacing whatever was loaded before.</summary>
    public void LoadCourse(string resourcePath)
    {
        try
        {
            // Fully qualified: `Course` is also the name of the property below.
            Course = Level.Course.Load(resourcePath);
        }
        catch (Exception ex)
        {
            // A broken course file is a content error, not a crash: report it
            // clearly and leave the previous course (if any) in place.
            GD.PushError($"Isolith: {ex.Message}");
            return;
        }

        CoursePath = resourcePath;
        Rebuild();
    }

    /// <summary>Tears the course down and builds it again from the loaded data.</summary>
    private void Rebuild()
    {
        if (Course is not { } course)
            return;

        foreach (Node child in _courseRoot.GetChildren())
        {
            // Detached first so the fresh build cannot collide by name with
            // nodes that are queued for deletion but still in the tree.
            _courseRoot.RemoveChild(child);
            child.QueueFree();
        }

        _built = CourseBuilder.Build(course, _courseRoot);

        foreach (Shard shard in _built.Shards)
            shard.Collected += OnShardCollected;

        foreach (Checkpoint checkpoint in _built.Checkpoints)
            checkpoint.Activated += OnCheckpointActivated;

        _built.Goal.Reached += OnGoalReached;

        foreach (Hazard hazard in FindHazards(_built.Root))
            hazard.Touched += Die;

        Stats = new RunStats
        {
            CourseId = course.Id,
            CourseHash = course.Hash,
            ShardsTotal = course.ShardCount,
            StartedAt = DateTimeOffset.UtcNow,
        };

        _elapsedSeconds = 0;
        _dying = false;
        _respawnPoint = _built.Spawn;

        _player.InputLocked = _uiFocused;
        _player.Respawn(_respawnPoint);
        _camera.SnapToTarget();

        SetState(GameState.Playing);
        StatsChanged?.Invoke(Stats);
    }

    /// <summary>Restarts the current course from scratch, resetting the timer.</summary>
    public void Restart()
    {
        GetTree().Paused = false;
        Rebuild();
    }

    // -----------------------------------------------------------------------
    // Events
    // -----------------------------------------------------------------------

    private void OnShardCollected(Shard shard)
    {
        Stats.ShardsCollected++;
        Audio.Play(Sfx.Collect);
        StatsChanged?.Invoke(Stats);
    }

    private void OnCheckpointActivated(Checkpoint checkpoint)
    {
        _respawnPoint = checkpoint.RespawnPoint;
    }

    private void OnJumped(bool fromBouncePad)
    {
        Stats.Jumps++;
        Audio.Play(fromBouncePad ? Sfx.Bounce : Sfx.Jump, volumeDb: -4.0f);
    }

    private void OnLanded(float impactSpeed)
    {
        // Only voice landings with some force behind them; every small hop
        // triggering a thud gets noisy fast.
        if (impactSpeed > 6.0f)
            Audio.Play(Sfx.Land, volumeDb: -8.0f);
    }

    private void OnGoalReached()
    {
        if (State == GameState.Complete)
            return;

        Stats.Completed = true;
        Stats.TimeMs = (int)(_elapsedSeconds * 1000.0);

        _player.InputLocked = true;
        SetState(GameState.Complete);

        Audio.Play(Sfx.Complete);

        // Saved locally first, unconditionally. Anything else that wants the
        // run — including repo sync — reacts to the event afterwards.
        RunHistory.Record(Stats);
        RunCompleted?.Invoke(Stats);
    }

    private void CheckKillPlane()
    {
        if (Course is not null && _player.GlobalPosition.Y < Course.KillPlaneY)
            Die();
    }

    /// <summary>Kills the player and returns them to the last checkpoint.</summary>
    public void Die()
    {
        if (_dying || State != GameState.Playing)
            return;

        _dying = true;
        Stats.Deaths++;
        Audio.Play(Sfx.Death, volumeDb: -3.0f);
        StatsChanged?.Invoke(Stats);

        _player.Respawn(_respawnPoint);
        _camera.SnapToTarget();

        // One frame of grace stops a respawn point overlapping a hazard from
        // looping deaths.
        CallDeferred(nameof(ClearDying));
    }

    private void ClearDying() => _dying = false;

    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------

    /// <summary>
    /// Tells the game a UI panel has keyboard focus, so movement keys typed into
    /// a text field don't also drive the character.
    /// </summary>
    public void SetUiFocus(bool focused)
    {
        _uiFocused = focused;
        _player.InputLocked = focused || State == GameState.Complete;
    }

    /// <summary>Pauses or resumes, unless the course is already finished.</summary>
    public void TogglePause()
    {
        if (State == GameState.Complete)
            return;

        bool pausing = State == GameState.Playing;
        GetTree().Paused = pausing;
        SetState(pausing ? GameState.Paused : GameState.Playing);
    }

    private void SetState(GameState state)
    {
        if (State == state)
            return;

        State = state;
        StateChanged?.Invoke(state);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IEnumerable<Hazard> FindHazards(Node root)
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is Hazard hazard)
                yield return hazard;
        }
    }

    private T RequireChild<T>(string name) where T : Node
    {
        T? node = GetNodeOrNull<T>(name);

        if (node is null)
        {
            throw new InvalidOperationException(
                $"Isolith: '{Name}' expects a child named '{name}' of type {typeof(T).Name}. " +
                "See the scene layout documented on GameManager.");
        }

        return node;
    }
}
