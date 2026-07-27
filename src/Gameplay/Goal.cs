using System;
using Godot;

namespace Isolith.Gameplay;

/// <summary>The finish pad. Fires once per attempt when the player lands on it.</summary>
[GlobalClass]
public partial class Goal : Area3D
{
    private const float PulseSpeed = 2.2f;

    private float _time;
    private bool _reached;

    /// <summary>The pad mesh, gently pulsed to draw the eye.</summary>
    public MeshInstance3D? Visual { get; set; }

    /// <summary>Raised once when the player reaches the goal.</summary>
    public event Action? Reached;

    public override void _Ready() => BodyEntered += OnBodyEntered;

    public override void _Process(double delta)
    {
        _time += (float)delta;

        if (Visual is null)
            return;

        float scale = 1.0f + Mathf.Sin(_time * PulseSpeed) * 0.05f;
        Visual.Scale = new Vector3(scale, 1.0f, scale);
    }

    private void OnBodyEntered(Node3D body)
    {
        if (_reached || body is not PlayerController)
            return;

        _reached = true;
        Reached?.Invoke();
    }

    /// <summary>Re-arms the goal for another attempt.</summary>
    public void Reset() => _reached = false;
}
