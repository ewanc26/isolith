using System;
using Godot;
using Isolith.Core;
using Isolith.Level;

namespace Isolith.Gameplay;

/// <summary>
/// The player character: camera-relative movement with the forgiveness a
/// platformer needs — coyote time, jump buffering, and variable jump height.
/// </summary>
/// <remarks>
/// Input is interpreted in the camera's frame rather than the world's, so
/// "forward" always means "up the screen" no matter which of the four
/// isometric views is active. Without that, a 45°-rotated camera makes every
/// direction feel diagonal.
/// </remarks>
[GlobalClass]
public partial class PlayerController : CharacterBody3D
{
    // --- Horizontal movement -------------------------------------------------

    [Export] public float MoveSpeed { get; set; } = 7.0f;
    [Export] public float GroundAcceleration { get; set; } = 65.0f;
    [Export] public float GroundFriction { get; set; } = 55.0f;
    [Export] public float AirAcceleration { get; set; } = 32.0f;
    [Export] public float AirFriction { get; set; } = 6.0f;

    // --- Jumping -------------------------------------------------------------

    /// <summary>Apex height, in metres, of a fully held jump.</summary>
    [Export] public float JumpHeight { get; set; } = 2.45f;

    /// <summary>Apex height of a bounce pad launch.</summary>
    [Export] public float BounceHeight { get; set; } = 5.5f;

    /// <summary>Gravity while moving upward. Lower than <see cref="FallGravity"/> for a floaty rise.</summary>
    [Export] public float RiseGravity { get; set; } = 22.0f;

    /// <summary>Gravity while falling. Higher than the rise for a snappy descent.</summary>
    [Export] public float FallGravity { get; set; } = 36.0f;

    [Export] public float MaxFallSpeed { get; set; } = 30.0f;

    /// <summary>How long after walking off a ledge a jump still counts.</summary>
    [Export] public float CoyoteTime { get; set; } = 0.12f;

    /// <summary>How long before landing a jump press is remembered.</summary>
    [Export] public float JumpBufferTime { get; set; } = 0.14f;

    /// <summary>Fraction of upward speed kept when the jump key is released early.</summary>
    [Export] public float JumpCutFactor { get; set; } = 0.42f;

    /// <summary>How quickly the character turns to face its heading, in turns/second.</summary>
    [Export] public float TurnSpeed { get; set; } = 12.0f;

    // --- Wiring --------------------------------------------------------------

    /// <summary>The camera whose yaw defines "forward". Set by the game manager.</summary>
    public IsometricCamera? Camera { get; set; }

    /// <summary>The mesh root, squashed and stretched for weight.</summary>
    public Node3D? Visual { get; set; }

    /// <summary>Raised when the player jumps, including bounce-pad launches.</summary>
    public event Action<bool>? Jumped;

    /// <summary>Raised on landing, with the vertical speed at impact.</summary>
    public event Action<float>? Landed;

    /// <summary>True while the player is frozen (course complete, paused, respawning).</summary>
    public bool InputLocked { get; set; }

    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private bool _wasOnFloor;
    private float _previousFallSpeed;
    private float _facing;
    private Vector3 _visualScale = Vector3.One;

    /// <summary>Initial upward speed needed to reach <see cref="JumpHeight"/>.</summary>
    private float JumpVelocity => Mathf.Sqrt(2.0f * RiseGravity * JumpHeight);

    private float BounceVelocity => Mathf.Sqrt(2.0f * RiseGravity * BounceHeight);

    public override void _Ready()
    {
        CollisionLayer = CourseBuilder.Mask(CourseBuilder.Layers.Player);
        CollisionMask = CourseBuilder.Mask(CourseBuilder.Layers.World);

        // Keeps the character glued to downward slopes and moving platforms
        // instead of stepping off them into a brief fall.
        FloorSnapLength = 0.4f;
        FloorMaxAngle = Mathf.DegToRad(50.0f);
        Visual ??= GetNodeOrNull<Node3D>("Visual");

        if (Visual is not null)
            _visualScale = Visual.Scale;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        UpdateTimers(dt);
        ApplyGravity(dt);
        ApplyHorizontal(dt);
        TryJump();

        Vector3 before = Velocity;
        MoveAndSlide();

        HandleLanding(before);
        HandleBouncePads();
        UpdateFacing(dt);
        UpdateSquashStretch(dt);

        _wasOnFloor = IsOnFloor();
        _previousFallSpeed = Velocity.Y;
    }

    // -----------------------------------------------------------------------
    // Movement
    // -----------------------------------------------------------------------

    private void UpdateTimers(float dt)
    {
        _coyoteTimer = IsOnFloor() ? CoyoteTime : Mathf.Max(0f, _coyoteTimer - dt);

        if (!InputLocked && Input.IsActionJustPressed(GameInput.Jump))
            _jumpBufferTimer = JumpBufferTime;
        else
            _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - dt);
    }

    private void ApplyGravity(float dt)
    {
        if (IsOnFloor() && Velocity.Y <= 0f)
        {
            // A small downward bias keeps IsOnFloor() stable frame to frame.
            Velocity = Velocity with { Y = -2.0f };
            return;
        }

        float gravity = Velocity.Y > 0f ? RiseGravity : FallGravity;
        float y = Velocity.Y - gravity * dt;

        // Releasing jump mid-rise cuts the arc short — the difference between a
        // tap and a hold.
        if (Velocity.Y > 0f && !InputLocked && Input.IsActionJustReleased(GameInput.Jump))
            y *= JumpCutFactor;

        Velocity = Velocity with { Y = Mathf.Max(y, -MaxFallSpeed) };
    }

    private void ApplyHorizontal(float dt)
    {
        Vector3 desired = InputLocked ? Vector3.Zero : ReadMoveInput() * MoveSpeed;
        Vector2 planar = new(Velocity.X, Velocity.Z);
        Vector2 target = new(desired.X, desired.Z);

        bool grounded = IsOnFloor();
        float rate = target.LengthSquared() > 0.01f
            ? (grounded ? GroundAcceleration : AirAcceleration)
            : (grounded ? GroundFriction : AirFriction);

        planar = planar.MoveToward(target, rate * dt);
        Velocity = new Vector3(planar.X, Velocity.Y, planar.Y);
    }

    /// <summary>
    /// Converts raw stick/key input into a world-space direction in the
    /// camera's frame, flattened onto the ground plane.
    /// </summary>
    private Vector3 ReadMoveInput()
    {
        Vector2 raw = Input.GetVector(
            GameInput.MoveLeft, GameInput.MoveRight,
            GameInput.MoveForward, GameInput.MoveBack);

        if (raw.LengthSquared() < 0.0001f)
            return Vector3.Zero;

        float yaw = Camera?.Yaw ?? 0f;
        Vector3 forward = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)).Normalized();
        Vector3 right = new Vector3(forward.Z, 0, -forward.X);

        // raw.Y is negative for "up" in Godot's convention, which is forward.
        Vector3 direction = right * raw.X + forward * -raw.Y;
        return direction.LimitLength(1.0f);
    }

    private void TryJump()
    {
        if (InputLocked || _jumpBufferTimer <= 0f || _coyoteTimer <= 0f)
            return;

        Velocity = Velocity with { Y = JumpVelocity };
        _jumpBufferTimer = 0f;
        _coyoteTimer = 0f;

        Stretch();
        Jumped?.Invoke(false);
    }

    // -----------------------------------------------------------------------
    // Reactions
    // -----------------------------------------------------------------------

    private void HandleLanding(Vector3 velocityBeforeSlide)
    {
        if (IsOnFloor() && !_wasOnFloor)
        {
            Squash(Mathf.Abs(_previousFallSpeed));
            Landed?.Invoke(Mathf.Abs(velocityBeforeSlide.Y));
        }
    }

    /// <summary>
    /// Launches the player when they land on a body flagged as a bounce pad.
    /// The flag lives on the body's metadata so the course builder decides what
    /// bounces, not this controller.
    /// </summary>
    private void HandleBouncePads()
    {
        if (!IsOnFloor() || Velocity.Y > 0.1f)
            return;

        for (int i = 0; i < GetSlideCollisionCount(); i++)
        {
            KinematicCollision3D collision = GetSlideCollision(i);

            if (collision.GetCollider() is not GodotObject collider ||
                collider is not Node node ||
                !node.HasMeta(CourseBuilder.BounceMeta))
            {
                continue;
            }

            Velocity = Velocity with { Y = BounceVelocity };
            _coyoteTimer = 0f;

            Stretch();
            Jumped?.Invoke(true);
            return;
        }
    }

    private void UpdateFacing(float dt)
    {
        if (Visual is null)
            return;

        Vector2 planar = new(Velocity.X, Velocity.Z);
        if (planar.LengthSquared() > 0.4f)
            _facing = Mathf.Atan2(planar.X, planar.Y);

        Visual.Rotation = Visual.Rotation with
        {
            Y = Mathf.LerpAngle(Visual.Rotation.Y, _facing, Mathf.Min(1.0f, TurnSpeed * dt)),
        };
    }

    // -----------------------------------------------------------------------
    // Squash and stretch — cheap weight, no animation data required
    // -----------------------------------------------------------------------

    private Vector3 _squashTarget = Vector3.One;

    private void Stretch() => _squashTarget = new Vector3(0.82f, 1.28f, 0.82f);

    private void Squash(float impactSpeed)
    {
        float amount = Mathf.Clamp(impactSpeed / MaxFallSpeed, 0.15f, 0.6f);
        _squashTarget = new Vector3(1.0f + amount * 0.5f, 1.0f - amount * 0.5f, 1.0f + amount * 0.5f);
    }

    private void UpdateSquashStretch(float dt)
    {
        if (Visual is null)
            return;

        _squashTarget = _squashTarget.Lerp(Vector3.One, Mathf.Min(1.0f, 9.0f * dt));
        Visual.Scale = Visual.Scale.Lerp(_visualScale * _squashTarget, Mathf.Min(1.0f, 18.0f * dt));
    }

    // -----------------------------------------------------------------------
    // External control
    // -----------------------------------------------------------------------

    /// <summary>Teleports the player and clears all momentum and jump state.</summary>
    public void Respawn(Vector3 position)
    {
        Velocity = Vector3.Zero;
        GlobalPosition = position;
        _coyoteTimer = 0f;
        _jumpBufferTimer = 0f;
        _wasOnFloor = false;
        _previousFallSpeed = 0f;
        _squashTarget = Vector3.One;

        if (Visual is not null)
            Visual.Scale = _visualScale;
    }
}
