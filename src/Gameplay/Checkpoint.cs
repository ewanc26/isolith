using System;
using Godot;
using Isolith.Level;

namespace Isolith.Gameplay;

/// <summary>
/// A respawn point. Lights up when first reached and stays lit; dying returns
/// the player to the most recently activated one.
/// </summary>
[GlobalClass]
public partial class Checkpoint : Area3D
{
    private bool _activated;

    /// <summary>The post mesh, recoloured on activation.</summary>
    public MeshInstance3D? Visual { get; set; }

    /// <summary>Raised the first time the player reaches this checkpoint.</summary>
    public event Action<Checkpoint>? Activated;

    /// <summary>Where the player reappears — just above the marker's base.</summary>
    public Vector3 RespawnPoint => GlobalPosition + new Vector3(0, 1.2f, 0);

    public override void _Ready() => BodyEntered += OnBodyEntered;

    private void OnBodyEntered(Node3D body)
    {
        if (_activated || body is not PlayerController)
            return;

        _activated = true;

        if (Visual is not null)
            Visual.MaterialOverride = Palette.CheckpointLit;

        Activated?.Invoke(this);
    }

    /// <summary>Returns the checkpoint to its unlit state.</summary>
    public void Reset()
    {
        _activated = false;

        if (Visual is not null)
            Visual.MaterialOverride = Palette.CheckpointIdle;
    }
}
