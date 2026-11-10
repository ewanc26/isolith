using System;
using System.Collections.Generic;
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

    // --- Ice surfaces ---------------------------------------------------------

    /// <summary>Ground acceleration while standing on ice. Deliberately mushy.</summary>
    [Export] public float IceAcceleration { get; set; } = 14.0f;

    /// <summary>
    /// Ground friction while standing on ice. A small fraction of the normal
    /// value, so momentum carries and stopping takes planning rather than
    /// happening the moment the stick is released.
    /// </summary>
    [Export] public float IceFriction { get; set; } = 3.0f;

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

    // --- Dashing -------------------------------------------------------------

    /// <summary>Horizontal speed held for the duration of a dash.</summary>
    [Export] public float DashSpeed { get; set; } = 13.0f;

    /// <summary>Seconds a dash lasts.</summary>
    [Export] public float DashDuration { get; set; } = 0.18f;

    /// <summary>Seconds before another dash is allowed.</summary>
    [Export] public float DashCooldown { get; set; } = 0.85f;

    // --- Wind columns ---------------------------------------------------------

    /// <summary>
    /// Upward acceleration while inside a vent, applied to both falling and
    /// rising motion: a player who falls in is caught, then lifted.
    /// </summary>
    [Export] public float VentLift { get; set; } = 28.0f;

    /// <summary>
    /// The fastest the vent will raise the player, in m/s — however hard the
    /// launch was. Capping the rise is what makes hovering stable and keeps a
    /// bounce pad dumped straight into a vent from rocketing out the top.
    /// </summary>
    [Export] public float VentRiseSpeed { get; set; } = 5.0f;

    private int _ventCount;

    /// <summary>True while the player is inside at least one wind column.</summary>
    public bool InVent => _ventCount > 0;

    /// <summary>Called by an <see cref="Updraft"/> when the player enters it.</summary>
    public void EnterVent() => _ventCount++;

    /// <summary>
    /// Called by an <see cref="Updraft"/> when the player leaves it, or when
    /// the vent itself is torn down mid-rebuild (see <see cref="Updraft"/>).
    /// Guarded so an exit can never take the count negative.
    /// </summary>
    public void ExitVent()
    {
        if (_ventCount > 0)
            _ventCount--;
    }

    // --- Gust wind ------------------------------------------------------------

    /// <summary>
    /// How quickly the wind's planar drag takes hold, in m/s per second. It
    /// has to beat ground friction (55) or a standing player would never be
    /// swept at all; on ice (friction 3) the wind barely even has to argue.
    /// </summary>
    [Export] public float GustAcceleration { get; set; } = 75.0f;

    private readonly List<GustField> _gusts = new();

    /// <summary>True while the player is inside at least one gust.</summary>
    public bool InGust => _gusts.Count > 0;

    /// <summary>Called by a <see cref="GustField"/> when the player enters it.</summary>
    public void EnterGust(GustField field)
    {
        if (!_gusts.Contains(field))
            _gusts.Add(field);
    }

    /// <summary>
    /// Called by a <see cref="GustField"/> when the player leaves it, or when
    /// the field is torn down mid-rebuild (see <see cref="GustField"/>).
    /// Removing the field itself, not a count, keeps overlapping gusts honest.
    /// </summary>
    public void ExitGust(GustField field) => _gusts.Remove(field);

    // --- Wiring --------------------------------------------------------------

    /// <summary>The camera whose yaw defines "forward". Set by the game manager.</summary>
    public IsometricCamera? Camera { get; set; }

    /// <summary>The mesh root, squashed and stretched for weight.</summary>
    public Node3D? Visual { get; set; }

    /// <summary>Raised when the player jumps, including bounce-pad launches.</summary>
    public event Action<bool>? Jumped;

    /// <summary>Raised on landing, with the vertical speed at impact.</summary>
    public event Action<float>? Landed;

    /// <summary>Raised when a dash starts, for sound and particles.</summary>
    public event Action? Dashed;

    /// <summary>True while the player is frozen (course complete, paused, respawning).</summary>
    public bool InputLocked { get; set; }

    /// <summary>
    /// The body the player is currently standing on, or was last standing on.
    /// </summary>
    /// <remarks>
    /// Used by endless generation to attribute a death to the thing that caused
    /// it — falling off a moving platform and mistiming a plain jump are
    /// different failures and the director responds to them differently.
    /// </remarks>
    public Node3D? LastFloor { get; private set; }

    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private float _dashTimer;
    private float _dashCooldownTimer;
    private Vector3 _dashDirection;
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
        TryStartDash();
        ApplyHorizontal(dt);
        TryJump();

        Vector3 before = Velocity;
        MoveAndSlide();

        TrackFloor();
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
        _dashTimer = Mathf.Max(0f, _dashTimer - dt);
        _dashCooldownTimer = Mathf.Max(0f, _dashCooldownTimer - dt);

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

        // Wind replaces gravity entirely while the player is inside the
        // column: caught if falling, lifted if slow, and never raised past the
        // vent's ceiling. Entering with a bounce's full launch speed is
        // therefore worth nothing extra — the vent is a hover tool, not a
        // second catapult.
        if (InVent)
        {
            float lifted = Mathf.Min(Velocity.Y + VentLift * dt, VentRiseSpeed);
            Velocity = Velocity with { Y = lifted };
            return;
        }

        float risingVelocity = Velocity.Y;

        // Releasing jump mid-rise cuts the arc short — the difference between a
        // tap and a hold. Applied before gravity, not after, so the cut acts on
        // the full rising velocity rather than on what's left once gravity has
        // already eaten into it this frame.
        if (risingVelocity > 0f && !InputLocked && Input.IsActionJustReleased(GameInput.Jump))
            risingVelocity *= JumpCutFactor;

        float gravity = risingVelocity > 0f ? RiseGravity : FallGravity;
        float y = risingVelocity - gravity * dt;

        Velocity = Velocity with { Y = Mathf.Max(y, -MaxFallSpeed) };
    }

    private void ApplyHorizontal(float dt)
    {
        // During a dash the controller is out of the loop: the burst is the
        // whole point of the move, and letting acceleration or friction fight
        // it would make the speed depend on whatever was held beforehand.
        if (_dashTimer > 0f)
        {
            Velocity = new Vector3(
                _dashDirection.X * DashSpeed,
                Velocity.Y,
                _dashDirection.Z * DashSpeed);
            return;
        }

        Vector3 desired = InputLocked ? Vector3.Zero : ReadMoveInput() * MoveSpeed;
        Vector2 planar = new(Velocity.X, Velocity.Z);
        Vector2 target = new(desired.X, desired.Z);

        // Which surface the player is on changes how the ground responds:
        // ice steals nearly all steering and all stopping power, which is the
        // whole point of the block. Detected through metadata so the course
        // builder decides what is slippery, not this controller.
        bool grounded = IsOnFloor();
        bool onIce = grounded && StandingOn(CourseBuilder.IceMeta);
        Vector3 flow = grounded ? ConveyorOnFloor() : Vector3.Zero;

        // The express design goal of a conveyor is how far it reaches: riding
        // one onto ice means the belt keeps feeding velocity in while the ice
        // takes away the grip needed to fight it.
        if (grounded)
            target += new Vector2(flow.X, flow.Z);

        float rate = target.LengthSquared() > 0.01f
            ? grounded ? (onIce ? IceAcceleration : GroundAcceleration) : AirAcceleration
            : grounded ? (onIce ? IceFriction : GroundFriction) : AirFriction;

        planar = planar.MoveToward(target, rate * dt);

        // Gust, the wind-frame rule: the flow is a speed the wind is willing
        // to bring the player to, not a force added to whatever they have.
        // A dash or a belt-ride carrying more speed than the wind is left
        // entirely alone, so the stack only composes in one direction — and
        // below that speed the wind decides where the slow go.
        if (_gusts.Count > 0)
        {
            // Overlapping gusts are a content choice; the first entered wins,
            // which keeps the push deterministic frame to frame.
            Vector3 wind = _gusts[0].Flow;
            var blowing = new Vector2(wind.X, wind.Z);
            if (planar.LengthSquared() < blowing.LengthSquared())
                planar = planar.MoveToward(blowing, GustAcceleration * dt);
        }

        Velocity = new Vector3(planar.X, Velocity.Y, planar.Y);
    }

    /// <summary>
    /// The belt flow of any conveyor under the player's feet, or zero.
    /// </summary>
    /// <remarks>
    /// Multiple conveyors overlapping are a content choice; the first found
    /// wins, which keeps the result deterministic frame to frame.
    /// </remarks>
    private Vector3 ConveyorOnFloor()
    {
        for (int i = 0; i < GetSlideCollisionCount(); i++)
        {
            if (GetSlideCollision(i).GetCollider() is Node node
                && node.HasMeta(CourseBuilder.ConveyorMeta))
            {
                return node.GetMeta(CourseBuilder.ConveyorMeta).AsVector3();
            }
        }

        return Vector3.Zero;
    }

    /// <summary>
    /// True when a body flagged with <paramref name="meta"/> is among the
    /// current floor contacts. Uses the previous slide's collisions, like the
    /// bounce-pad check, because that is the freshest resolved contact data.
    /// </summary>
    private bool StandingOn(string meta)
    {
        for (int i = 0; i < GetSlideCollisionCount(); i++)
        {
            if (GetSlideCollision(i).GetCollider() is Node node && node.HasMeta(meta))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Starts a dash in the current movement direction, or straight ahead when
    /// no direction is held.
    /// </summary>
    /// <remarks>
    /// A dash is strictly additive to the traversal the physics envelope
    /// describes: it never shortens a jump and generated courses are clamped
    /// against a normal jump, so adding this cannot make a section
    /// uncrossable. It can only make one easier.
    /// </remarks>
    private void TryStartDash()
    {
        if (InputLocked || _dashTimer > 0f || _dashCooldownTimer > 0f)
            return;

        if (!Input.IsActionJustPressed(GameInput.Dash))
            return;

        Vector3 direction = ReadMoveInput();

        if (direction.LengthSquared() < 0.0001f)
            direction = new Vector3(Mathf.Sin(_facing), 0f, Mathf.Cos(_facing));

        _dashDirection = direction.Normalized();
        _dashTimer = DashDuration;
        _dashCooldownTimer = DashCooldown;

        Velocity = new Vector3(
            _dashDirection.X * DashSpeed,
            Velocity.Y,
            _dashDirection.Z * DashSpeed);

        Stretch();
        Dashed?.Invoke();
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

    /// <summary>Remembers the current floor, keeping the last one while airborne.</summary>
    private void TrackFloor()
    {
        if (!IsOnFloor())
            return;

        for (int i = 0; i < GetSlideCollisionCount(); i++)
        {
            if (GetSlideCollision(i).GetCollider() is Node3D body)
            {
                LastFloor = body;
                return;
            }
        }
    }

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
        _dashTimer = 0f;
        _dashCooldownTimer = 0f;
        _dashDirection = Vector3.Zero;
        _wasOnFloor = false;
        _previousFallSpeed = 0f;
        _squashTarget = Vector3.One;

        if (Visual is not null)
            Visual.Scale = _visualScale;
    }
}
