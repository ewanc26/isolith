using System;
using Godot;
using Isolith.Level;

namespace Isolith.Gameplay;

/// <summary>
/// A rising wind column. The player falls through its mouth, is lifted while
/// inside, and caps at <see cref="PlayerController.VentRiseSpeed"/> however
/// hard they came in.
/// </summary>
/// <remarks>
/// Like hazards and bounce pads this is an <see cref="Area3D"/>, not collision:
/// the point of the vent is that it moves the player through the air, so it
/// must never be something they can stand on.
///
/// Overlap is counted on the player (enter/exit, not per-frame polling) so the
/// controller's physics stay deterministic, and the vent releases its hold
/// both when the body exits and when the vent itself leaves the tree — a
/// course rebuild while the player is inside would otherwise leave their lift
/// stuck on for the rest of the session.
/// </remarks>
[GlobalClass]
public partial class Updraft : Area3D
{
    private PlayerController? _lifter;

    public override void _Ready()
    {
        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        Release();
    }

    private void OnBodyEntered(Node3D body)
    {
        if (body is PlayerController player)
        {
            _lifter = player;
            player.EnterVent();
        }
    }

    private void OnBodyExited(Node3D body)
    {
        if (body is PlayerController)
            Release();
    }

    /// <summary>Releases the hold once, rather than per overlapping node.</summary>
    private void Release()
    {
        if (_lifter is not PlayerController player)
            return;

        if (IsInstanceValid(player))
            player.ExitVent();

        _lifter = null;
    }
}
