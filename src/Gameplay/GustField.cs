using System;
using Godot;

namespace Isolith.Gameplay;

/// <summary>
/// A horizontal wind wall. The player keeps their own momentum, but the wind
/// drags them along its flow vector while they are inside — crossing it
/// aimlessly means arriving somewhere they did not choose, and a dash or a
/// conveyor faster than the wind is nudged by nothing.
/// </summary>
/// <remarks>
/// Like the vent this is an <see cref="Area3D"/>, not collision: wind that
/// could be stood on would be a wall. The player tracks the fields it is
/// inside, so a course rebuilt mid-flight releases its hold without an exit
/// event that will now never come; the same guarantee as <see cref="Updraft"/>.
/// </remarks>
[GlobalClass]
public partial class GustField : Area3D
{
    private PlayerController? _pushed;

    /// <summary>The wind's flow in the world frame: [x, 0, z] metres per second.</summary>
    public Vector3 Flow { get; private set; } = Vector3.Zero;

    /// <summary>Sets the flow the field pushes with, from the course data's <c>flow</c>.</summary>
    public void Configure(Vector3 flow) => Flow = flow with { Y = 0f };

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
            _pushed = player;
            player.EnterGust(this);
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
        if (_pushed is not PlayerController player)
            return;

        player.ExitGust(this);
        _pushed = null;
    }
}
