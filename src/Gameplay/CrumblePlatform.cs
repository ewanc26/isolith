using Godot;
using Isolith.Level;

namespace Isolith.Gameplay;

/// <summary>
/// A platform that gives way shortly after the player stands on it, then
/// rebuilds itself so a retry is always possible.
/// </summary>
[GlobalClass]
public partial class CrumblePlatform : AnimatableBody3D
{
    private const float WarnSeconds = 0.55f;    // shake before dropping
    private const float FallSeconds = 1.1f;     // visible fall
    private const float RestoreSeconds = 2.2f;  // gone, then back

    private enum State { Idle, Warning, Falling, Gone }

    private State _state = State.Idle;
    private float _timer;
    private Vector3 _home;
    private MeshInstance3D? _mesh;
    private CollisionShape3D? _shape;

    /// <summary>Sets up the platform before it enters the tree.</summary>
    public void Configure(Vector3 size)
    {
        Name = "CrumblePlatform";
        SyncToPhysics = true;
        CollisionLayer = CourseBuilder.Mask(CourseBuilder.Layers.World);
        CollisionMask = 0;

        _shape = CourseBuilder.BoxShape(size);
        AddChild(_shape);

        _mesh = CourseBuilder.BoxMesh(size, Palette.Fragile);
        AddChild(_mesh);

        // A trigger sitting just above the surface notices the player standing
        // here without the controller needing to know about crumbling at all.
        var trigger = new Area3D
        {
            Name = "Trigger",
            CollisionLayer = CourseBuilder.Mask(CourseBuilder.Layers.Trigger),
            CollisionMask = CourseBuilder.Mask(CourseBuilder.Layers.Player),
            Position = new Vector3(0, size.Y * 0.5f + 0.35f, 0),
        };
        trigger.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(size.X, 0.7f, size.Z) },
        });
        trigger.BodyEntered += OnSteppedOn;
        AddChild(trigger);
    }

    public override void _Ready() => _home = Position;

    public override void _PhysicsProcess(double delta)
    {
        if (_state == State.Idle)
            return;

        _timer += (float)delta;

        switch (_state)
        {
            case State.Warning:
                // Small horizontal jitter telegraphs the drop.
                float shake = Mathf.Sin(_timer * 60f) * 0.045f;
                Position = _home + new Vector3(shake, 0, shake * 0.6f);

                if (_timer >= WarnSeconds)
                    EnterFalling();
                break;

            case State.Falling:
                Position += new Vector3(0, -6.0f * (float)delta, 0);

                if (_timer >= FallSeconds)
                    EnterGone();
                break;

            case State.Gone:
                if (_timer >= RestoreSeconds)
                    Reset();
                break;
        }
    }

    private void OnSteppedOn(Node3D body)
    {
        if (_state != State.Idle || body is not PlayerController)
            return;

        _state = State.Warning;
        _timer = 0f;
    }

    private void EnterFalling()
    {
        _state = State.Falling;
        _timer = 0f;
        Position = _home;

        // Deferred: collision state cannot change during physics flushing.
        _shape?.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
    }

    private void EnterGone()
    {
        _state = State.Gone;
        _timer = 0f;
        Visible = false;
    }

    /// <summary>Restores the platform to its starting position and state.</summary>
    public void Reset()
    {
        _state = State.Idle;
        _timer = 0f;
        Position = _home;
        Visible = true;
        _shape?.SetDeferred(CollisionShape3D.PropertyName.Disabled, false);
    }
}
