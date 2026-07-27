using System;
using Godot;

namespace Isolith.Gameplay;

/// <summary>
/// A collectible. Spins and bobs so it reads as interactive at isometric
/// angles, and reports exactly once when the player touches it.
/// </summary>
[GlobalClass]
public partial class Shard : Area3D
{
    private const float SpinSpeed = 1.8f;   // radians/second
    private const float BobHeight = 0.22f;
    private const float BobSpeed = 2.4f;

    private float _time;
    private float _baseHeight;
    private bool _collected;

    /// <summary>The mesh to animate; assigned by the course builder.</summary>
    public MeshInstance3D? Visual { get; set; }

    /// <summary>Raised once, the first time the player enters.</summary>
    public event Action<Shard>? Collected;

    public override void _Ready()
    {
        _baseHeight = Position.Y;

        // Desynchronise the bob so a row of shards doesn't pulse in lockstep.
        _time = GlobalPosition.X * 0.7f + GlobalPosition.Z * 0.4f;

        BodyEntered += OnBodyEntered;
    }

    public override void _Process(double delta)
    {
        if (_collected)
            return;

        _time += (float)delta;

        if (Visual is not null)
            Visual.RotateY(SpinSpeed * (float)delta);

        Position = new Vector3(
            Position.X,
            _baseHeight + Mathf.Sin(_time * BobSpeed) * BobHeight,
            Position.Z);
    }

    private void OnBodyEntered(Node3D body)
    {
        if (_collected || body is not PlayerController)
            return;

        _collected = true;
        SetDeferred(Area3D.PropertyName.Monitoring, false);
        Visible = false;

        Collected?.Invoke(this);
    }

    /// <summary>Puts the shard back for a fresh attempt.</summary>
    public void Reset()
    {
        _collected = false;
        Visible = true;
        SetDeferred(Area3D.PropertyName.Monitoring, true);
    }
}
