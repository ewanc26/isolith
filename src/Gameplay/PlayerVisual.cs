using Godot;

namespace Isolith.Gameplay;

/// <summary>
/// Animates the player model by rotating its limb nodes.
/// </summary>
/// <remarks>
/// The model is segmented rather than skinned (see
/// <c>tools/generate_player_model.py</c>), so there is no armature and there are
/// no animation clips. Limbs are posed directly each frame from the character's
/// actual state.
///
/// That is a deliberate trade. A baked run cycle plays at a fixed rate and
/// desynchronises from real movement the moment the player is accelerating,
/// airborne, or being carried by a platform. Driving the phase from measured
/// horizontal speed means the legs always match the ground the character is
/// covering, and the airborne pose is a state rather than a clip that has to be
/// blended in.
///
/// Node names are the contract with the generator script.
/// </remarks>
[GlobalClass]
public partial class PlayerVisual : Node3D
{
    /// <summary>Strides per second at full running speed.</summary>
    [Export] public float StrideRate { get; set; } = 1.35f;

    /// <summary>Peak leg swing, in degrees, at full speed.</summary>
    [Export] public float LegSwing { get; set; } = 42.0f;

    /// <summary>Peak arm swing, in degrees, at full speed.</summary>
    [Export] public float ArmSwing { get; set; } = 34.0f;

    /// <summary>How quickly limbs settle toward their target pose.</summary>
    [Export] public float PoseSharpness { get; set; } = 16.0f;

    private PlayerController? _player;

    private Node3D? _torso;
    private Node3D? _head;
    private Node3D? _armL;
    private Node3D? _armR;
    private Node3D? _legL;
    private Node3D? _legR;

    private float _phase;
    private float _airborneBlend;
    private float _bob;

    // The pose the model was exported in. Animation is applied as a delta from
    // this rather than as an absolute transform: overwriting a limb's rotation
    // outright would discard whatever the importer set up, and overwriting its
    // position would move it off its joint.
    private readonly System.Collections.Generic.Dictionary<Node3D, Transform3D> _rest = new();

    public override void _Ready()
    {
        _player = GetParent<PlayerController>();

        _torso = Find("Torso");
        _head = Find("Head");
        _armL = Find("ArmL");
        _armR = Find("ArmR");
        _legL = Find("LegL");
        _legR = Find("LegR");

        foreach (Node3D? limb in new[] { _torso, _head, _armL, _armR, _legL, _legR })
        {
            if (limb is not null)
                _rest[limb] = limb.Transform;
        }

        if (_legL is null || _legR is null)
        {
            // The model failed to import or was renamed. Say so once, clearly,
            // rather than silently standing still for the rest of the session.
            GD.PushWarning(
                "Isolith: player model limbs not found. Regenerate with " +
                "'blender -b --python tools/generate_player_model.py'.");
        }
    }

    public override void _Process(double delta)
    {
        if (_player is null)
            return;

        float dt = (float)delta;

        var planar = new Vector2(_player.Velocity.X, _player.Velocity.Z);
        float speed = planar.Length();
        float normalised = Mathf.Clamp(speed / Mathf.Max(_player.MoveSpeed, 0.01f), 0f, 1f);

        bool grounded = _player.IsOnFloor();

        // Blend rather than switch, so leaving the ground does not snap the
        // legs into the tucked pose mid-stride.
        float airborneTarget = grounded ? 0f : 1f;
        _airborneBlend = Mathf.Lerp(_airborneBlend, airborneTarget, 1f - Mathf.Exp(-10f * dt));

        if (grounded)
        {
            // Phase advances with distance covered, not with time, so the feet
            // keep pace with the ground at any speed.
            _phase += normalised * StrideRate * Mathf.Tau * dt;
        }

        _bob += dt;

        PoseGround(normalised, dt);
        PoseAir(dt);
    }

    // -----------------------------------------------------------------------
    // Poses
    // -----------------------------------------------------------------------

    private void PoseGround(float speed, float dt)
    {
        float swing = Mathf.Sin(_phase);
        float counter = Mathf.Sin(_phase + Mathf.Pi);

        float legAmount = Mathf.DegToRad(LegSwing) * speed * (1f - _airborneBlend);
        float armAmount = Mathf.DegToRad(ArmSwing) * speed * (1f - _airborneBlend);

        // Idle: a slow breath so a standing character is not a statue.
        float idle = (1f - speed) * (1f - _airborneBlend);
        float breathe = Mathf.Sin(_bob * 1.6f) * 0.02f * idle;

        Approach(_legL, new Vector3(swing * legAmount, 0, 0), dt);
        Approach(_legR, new Vector3(counter * legAmount, 0, 0), dt);

        // Arms counter-swing against the legs, which is what makes a two-legged
        // run read as a run rather than a hop.
        Approach(_armL, new Vector3(counter * armAmount, 0, Mathf.DegToRad(-6)), dt);
        Approach(_armR, new Vector3(swing * armAmount, 0, Mathf.DegToRad(6)), dt);

        if (_torso is not null && _rest.TryGetValue(_torso, out Transform3D torsoRest))
        {
            // Lean into the run, and let the shoulders roll slightly with it.
            var lean = new Vector3(
                Mathf.DegToRad(9f) * speed * (1f - _airborneBlend),
                0,
                Mathf.Sin(_phase) * Mathf.DegToRad(3f) * speed);

            _torso.Position = torsoRest.Origin + new Vector3(0, breathe, 0);
            Approach(_torso, lean, dt);
        }

        if (_head is not null)
        {
            // The head counter-rotates against the torso lean so the character
            // keeps looking where it is going.
            Approach(_head, new Vector3(Mathf.DegToRad(-5f) * speed * (1f - _airborneBlend), 0, 0), dt);
        }
    }

    private void PoseAir(float dt)
    {
        if (_airborneBlend < 0.01f || _player is null)
            return;

        // Rising: legs trail and arms come up. Falling: legs reach for ground.
        float rising = Mathf.Clamp(_player.Velocity.Y / 8.0f, -1f, 1f);

        float tuck = Mathf.DegToRad(Mathf.Lerp(28f, -34f, (rising + 1f) * 0.5f));
        float reach = Mathf.DegToRad(Mathf.Lerp(-40f, -95f, (rising + 1f) * 0.5f));

        Blend(_legL, new Vector3(tuck, 0, 0), _airborneBlend, dt);
        Blend(_legR, new Vector3(tuck * 0.7f, 0, 0), _airborneBlend, dt);

        Blend(_armL, new Vector3(reach, 0, Mathf.DegToRad(-16)), _airborneBlend, dt);
        Blend(_armR, new Vector3(reach, 0, Mathf.DegToRad(16)), _airborneBlend, dt);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Approach(Node3D? node, Vector3 target, float dt)
    {
        if (node is null)
            return;

        float t = 1f - Mathf.Exp(-PoseSharpness * dt);
        node.Rotation = node.Rotation.Lerp(Rest(node) + target, t);
    }

    /// <summary>Pulls a limb toward an additional pose by <paramref name="weight"/>.</summary>
    private void Blend(Node3D? node, Vector3 target, float weight, float dt)
    {
        if (node is null)
            return;

        float t = (1f - Mathf.Exp(-PoseSharpness * dt)) * weight;
        node.Rotation = node.Rotation.Lerp(Rest(node) + target, t);
    }

    /// <summary>The exported rest rotation of a limb, or zero if it is unknown.</summary>
    private Vector3 Rest(Node3D node) =>
        _rest.TryGetValue(node, out Transform3D transform) ? transform.Basis.GetEuler() : Vector3.Zero;

    /// <summary>Finds a descendant by name, whatever depth the importer nested it at.</summary>
    private Node3D? Find(string name)
    {
        return FindChild(name, recursive: true, owned: false) as Node3D;
    }
}
