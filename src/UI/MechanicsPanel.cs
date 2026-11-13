using System;
using Godot;
using Isolith.Core;

namespace Isolith.UI;

/// <summary>
/// A reference screen for the controls and the block kinds the game can put
/// under the player.
/// </summary>
/// <remarks>
/// Purely informational, so it can be opened from the title screen or from a
/// paused run without touching session state. The surface list mirrors the
/// vocabulary in <see cref="Isolith.Level.BlockKind"/> so a player can meet ice,
/// a belt, a vent, and a gust here before one of them kills them.
///
/// Two columns rather than one scrolling list: on a pad, a screen whose only
/// focusable control is "Back" is easier to read than one that also asks the
/// player to scroll a list with the stick.
/// </remarks>
[GlobalClass]
public partial class MechanicsPanel : CenterContainer
{
    /// <summary>Raised when the player leaves the panel.</summary>
    public event Action? Closed;

    private Button _back = null!;

    public override void _Ready()
    {
        AnchorRight = 1;
        AnchorBottom = 1;
        ProcessMode = ProcessModeEnum.Always;

        // Built at construction so the focus chain exists even while hidden:
        // the smoke test checks every menu control has a neighbour without
        // opening each panel first.
        Build();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Guarded by Visible because this node stays in the tree while hidden,
        // and it must not swallow Back from the screen that is actually up.
        // GameInput.Pause is the same action the settings panel backs out with,
        // so Back is Start/Escape on both screens rather than two conventions.
        if (Visible && @event.IsActionPressed(GameInput.Pause))
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    /// <summary>Shows the panel and takes focus.</summary>
    public void Open()
    {
        Visible = true;
        MenuKit.Focus(_back);
    }

    private void Close()
    {
        Visible = false;
        Closed?.Invoke();
    }

    private void Build()
    {
        PanelContainer card = MenuKit.Card(640f);
        AddChild(card);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        card.AddChild(column);

        column.AddChild(MenuKit.Title("How to play", 30));

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 28);
        column.AddChild(columns);

        VBoxContainer controls = Section(columns, "Controls");
        Entry(controls, "Move", "Left stick or WASD. Movement is camera-relative, so up is always up the screen.");
        Entry(controls, "Jump", "A or Space. Hold for height, release early to cut it short.");
        Entry(controls, "Dash", "B or Shift. A short burst that adds reach, but no course requires it.");
        Entry(controls, "Turn view", "LB/RB or Q/E. Rotate the isometric view in 90° steps.");
        Entry(controls, "Shards", "Collect them for score. Checkpoints are where you respawn.");
        Entry(controls, "Goal", "Reach it to finish an authored course.");

        VBoxContainer surfaces = Section(columns, "Surfaces");
        Entry(surfaces, "Ice", "Almost no grip: momentum carries and steering feels mushy.");
        Entry(surfaces, "Conveyor", "Drags you along its flow, even when you fight it.");
        Entry(surfaces, "Vent", "A rising wind column that catches and lifts a falling player.");
        Entry(surfaces, "Gust", "A sidewind that pushes you, unless you arrive faster than it.");
        Entry(surfaces, "Crumble", "Shakes, drops, and returns a few seconds later.");
        Entry(surfaces, "Bounce", "Launches you far higher than a jump can reach.");
        Entry(surfaces, "Hazard", "Spikes. Not standable; touching them ends the attempt.");

        _back = MenuKit.MenuButton("Back", Close);
        column.AddChild(_back);

        column.AddChild(MenuKit.Caption(
            GameInput.UsingGamepad ? "Start to go back" : "Esc to go back"));

        MenuKit.FocusChain(_back);

        // Hidden until opened, like the settings panel. Built while hidden so
        // the focus chain exists for the smoke test, but never shown by default.
        Visible = false;
    }

    private static VBoxContainer Section(Control parent, string title)
    {
        var section = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        section.AddThemeConstantOverride("separation", 6);
        parent.AddChild(section);

        var heading = new Label { Text = title };
        heading.AddThemeFontSizeOverride("font_size", 16);
        heading.AddThemeColorOverride("font_color", MenuKit.Accent);
        section.AddChild(heading);

        return section;
    }

    private static void Entry(Control parent, string name, string detail)
    {
        var row = new VBoxContainer();
        row.AddThemeConstantOverride("separation", 0);
        parent.AddChild(row);

        var label = new Label { Text = name };
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", MenuKit.Ink);
        row.AddChild(label);

        Label detailLabel = MenuKit.Caption(detail, 12);
        detailLabel.HorizontalAlignment = HorizontalAlignment.Left;
        row.AddChild(detailLabel);
    }
}
