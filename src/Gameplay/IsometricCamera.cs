using Godot;
using Isolith.Core;

namespace Isolith.Gameplay;

/// <summary>
/// A true-isometric follow camera: orthographic, pitched to the classic
/// isometric angle, and rotatable in 90° steps so the player can see around
/// geometry that would otherwise hide the route.
/// </summary>
[GlobalClass]
public partial class IsometricCamera : Camera3D
{
    /// <summary>
    /// The isometric pitch, <c>atan(1/√2) ≈ 35.264°</c>. At this angle a unit
    /// cube projects to a regular hexagon and the three visible faces are
    /// equally foreshortened — the property that makes the projection read as
    /// isometric rather than merely angled.
    /// </summary>
    public static readonly float IsometricPitch = -Mathf.Atan(1.0f / Mathf.Sqrt2);

    [Export] public float FollowDistance { get; set; } = 40.0f;

    /// <summary>Orthographic view height in metres. Smaller is more zoomed in.</summary>
    [Export] public float ViewSize { get; set; } = 17.0f;

    [Export] public float MinViewSize { get; set; } = 9.0f;
    [Export] public float MaxViewSize { get; set; } = 30.0f;

    /// <summary>Horizontal follow smoothing, in units of "fraction closed per second".</summary>
    [Export] public float FollowSharpness { get; set; } = 9.0f;

    /// <summary>Vertical follow smoothing. Slower than horizontal so jumps don't jolt the view.</summary>
    [Export] public float VerticalSharpness { get; set; } = 5.0f;

    /// <summary>Seconds a 90° rotation takes.</summary>
    [Export] public float RotationDuration { get; set; } = 0.3f;

    /// <summary>Height above the target the camera aims at.</summary>
    [Export] public float FocusHeight { get; set; } = 1.0f;

    /// <summary>The node to follow. Set by the game manager.</summary>
    public Node3D? Target { get; set; }

    /// <summary>
    /// The yaw that movement input is interpreted against, in radians.
    /// </summary>
    /// <remarks>
    /// This is the <em>destination</em> yaw, not the animating one. Snapping the
    /// control frame the instant a rotation starts means a held direction stays
    /// meaningful throughout the turn; interpolating it instead would curve the
    /// player's path mid-rotation.
    /// </remarks>
    public float Yaw => _targetYaw;

    private float _currentYaw;
    private float _targetYaw;
    private float _rotationElapsed;
    private float _rotationFrom;
    private bool _rotating;
    private Vector3 _focus;
    private bool _hasFocus;

    public override void _Ready()
    {
        Projection = ProjectionType.Orthogonal;

        // Zoom is a player preference, not a scene property: whatever they last
        // zoomed to — in a previous session or on the settings slider — is what
        // the next run opens at.
        ViewSize = Mathf.Clamp(Settings.CameraZoom, MinViewSize, MaxViewSize);
        Size = ViewSize;

        // Orthographic depth range must comfortably contain the scene; the
        // camera is pulled far back so nothing clips at the near plane.
        Near = 0.1f;
        Far = FollowDistance * 3.0f;

        // Start at 45°, the canonical isometric yaw.
        _currentYaw = Mathf.Pi * 0.25f;
        _targetYaw = _currentYaw;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(GameInput.CameraRotateLeft))
            StartRotation(-Mathf.Pi * 0.5f);
        else if (@event.IsActionPressed(GameInput.CameraRotateRight))
            StartRotation(Mathf.Pi * 0.5f);
        else if (@event.IsActionPressed(GameInput.ZoomIn))
            AdjustZoom(-1.5f);
        else if (@event.IsActionPressed(GameInput.ZoomOut))
            AdjustZoom(1.5f);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        AdvanceRotation(dt);
        FollowTarget(dt);
        ApplyTransform();
    }

    private void StartRotation(float deltaYaw)
    {
        _rotationFrom = _currentYaw;
        _targetYaw += deltaYaw;
        _rotationElapsed = 0f;
        _rotating = true;
    }

    private void AdvanceRotation(float dt)
    {
        if (!_rotating)
            return;

        _rotationElapsed += dt;
        float t = Mathf.Clamp(_rotationElapsed / RotationDuration, 0f, 1f);

        // Smoothstep: no abrupt start or stop on the turn.
        float eased = t * t * (3.0f - 2.0f * t);
        _currentYaw = Mathf.Lerp(_rotationFrom, _targetYaw, eased);

        if (t >= 1.0f)
        {
            _currentYaw = _targetYaw;
            _rotating = false;
        }
    }

    private void FollowTarget(float dt)
    {
        if (Target is null)
            return;

        Vector3 desired = Target.GlobalPosition + new Vector3(0, FocusHeight, 0);

        if (!_hasFocus)
        {
            _focus = desired;
            _hasFocus = true;
            return;
        }

        // Exponential smoothing, made frame-rate independent so the camera
        // behaves identically at 60 and 144 Hz.
        float horizontal = 1.0f - Mathf.Exp(-FollowSharpness * dt);
        float vertical = 1.0f - Mathf.Exp(-VerticalSharpness * dt);

        _focus = new Vector3(
            Mathf.Lerp(_focus.X, desired.X, horizontal),
            Mathf.Lerp(_focus.Y, desired.Y, vertical),
            Mathf.Lerp(_focus.Z, desired.Z, horizontal));
    }

    private void ApplyTransform()
    {
        Rotation = new Vector3(IsometricPitch, _currentYaw, 0f);

        // Basis.Z points away from the view direction, so stepping along it
        // pulls the camera back from what it is looking at.
        GlobalPosition = _focus + Transform.Basis.Z * FollowDistance;
        Size = ViewSize;
    }

    private void AdjustZoom(float amount)
    {
        ViewSize = Mathf.Clamp(ViewSize + amount, MinViewSize, MaxViewSize);

        // Persisted, so the in-game zoom keys and the settings slider are two
        // views of one value rather than two settings that disagree.
        Settings.CameraZoom = ViewSize;
    }

    /// <summary>Drops the camera straight onto the target, skipping the follow lag.</summary>
    public void SnapToTarget()
    {
        if (Target is null)
            return;

        _focus = Target.GlobalPosition + new Vector3(0, FocusHeight, 0);
        _hasFocus = true;

        // A rotation in progress must not keep animating toward a yaw that no
        // longer means anything once the camera has jumped to a new position —
        // it would visibly spin on the spot after respawn/restart.
        _currentYaw = _targetYaw;
        _rotating = false;

        ApplyTransform();
    }
}
