using System;
using Godot;

namespace Isolith.Gameplay;

/// <summary>
/// A lethal volume. The player passes through it rather than standing on it,
/// so spikes read as something to fall into.
/// </summary>
[GlobalClass]
public partial class Hazard : Area3D
{
    /// <summary>Raised whenever the player enters the volume.</summary>
    public event Action? Touched;

    public override void _Ready() => BodyEntered += OnBodyEntered;

    private void OnBodyEntered(Node3D body)
    {
        if (body is PlayerController)
            Touched?.Invoke();
    }
}
