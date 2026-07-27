using Godot;
using Isolith.Level;

namespace Isolith.Gameplay;

/// <summary>
/// A platform shuttling between two points on a sine ease, so it slows at each
/// end instead of snapping direction.
/// </summary>
/// <remarks>
/// This is an <see cref="AnimatableBody3D"/> with <c>SyncToPhysics</c> enabled,
/// which is what lets Godot's <see cref="CharacterBody3D"/> inherit the
/// platform's motion — the player rides it without any code in the controller.
/// </remarks>
[GlobalClass]
public partial class MovingPlatform : AnimatableBody3D
{
    private Vector3 _from;
    private Vector3 _to;
    private float _period = 4.0f;
    private float _phase;
    private float _time;

    /// <summary>Sets up the platform before it enters the tree.</summary>
    /// <param name="period">Seconds for a full there-and-back cycle.</param>
    /// <param name="phase">Cycle offset in the range 0–1.</param>
    public void Configure(Vector3 from, Vector3 to, Vector3 size, float period, float phase)
    {
        Name = "MovingPlatform";
        _from = from;
        _to = to;
        _period = Mathf.Max(period, 0.1f);
        _phase = phase;

        SyncToPhysics = true;
        CollisionLayer = CourseBuilder.Mask(CourseBuilder.Layers.World);
        CollisionMask = 0;

        AddChild(CourseBuilder.BoxShape(size));
        AddChild(CourseBuilder.BoxMesh(size, Palette.Solid));

        Position = from;
    }

    public override void _PhysicsProcess(double delta)
    {
        _time += (float)delta;

        // 0 → 1 → 0 over one period, eased at both ends.
        float cycle = (_time / _period + _phase) * Mathf.Tau;
        float t = (1.0f - Mathf.Cos(cycle)) * 0.5f;

        Position = _from.Lerp(_to, t);
    }
}
