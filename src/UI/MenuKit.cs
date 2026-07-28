using System;
using Godot;
using Isolith.Level;

namespace Isolith.UI;

/// <summary>
/// Shared widget construction for every menu, so they look and behave alike.
/// </summary>
/// <remarks>
/// Menus are built in code for the same reason the HUD is (§5 of AGENTS.md).
/// The important part here is not the styling but the focus behaviour:
/// <b>gamepad is the primary input</b>, so every menu must be fully navigable
/// with a stick and two buttons, which means real focus neighbours, a visible
/// focus ring, and something focused the moment a menu opens. A menu you can
/// only use with a mouse is a broken menu in this project.
/// </remarks>
public static class MenuKit
{
    public static readonly Color Ink = new(0.94f, 0.95f, 0.98f);
    public static readonly Color Dim = new(0.94f, 0.95f, 0.98f, 0.62f);
    public static readonly Color Accent = Palette.Bounce;

    /// <summary>A dimmed full-screen backdrop, so the game reads as suspended.</summary>
    public static ColorRect Scrim()
    {
        return new ColorRect
        {
            Color = new Color(Palette.Sky, 0.82f),
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
    }

    /// <summary>The standard bordered card every menu sits in.</summary>
    public static PanelContainer Card(float minWidth = 460f)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(minWidth, 0) };

        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(Palette.Sky, 0.97f),
            BorderColor = new Color(1, 1, 1, 0.14f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14,
            ContentMarginLeft = 34,
            ContentMarginRight = 34,
            ContentMarginTop = 28,
            ContentMarginBottom = 28,
        });

        return panel;
    }

    public static Label Title(string text, int size = 44)
    {
        var label = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Ink);
        return label;
    }

    public static Label Caption(string text, int size = 14)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Dim);
        return label;
    }

    /// <summary>A menu button, styled and wired to <paramref name="onPressed"/>.</summary>
    public static Button MenuButton(string text, Action onPressed)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, 44),
            FocusMode = Control.FocusModeEnum.All,
        };

        button.AddThemeFontSizeOverride("font_size", 18);
        button.AddThemeColorOverride("font_color", Ink);
        button.AddThemeColorOverride("font_hover_color", Accent);
        button.AddThemeColorOverride("font_focus_color", Accent);

        button.AddThemeStyleboxOverride("normal", ButtonBox(new Color(1, 1, 1, 0.05f), Colors.Transparent));
        button.AddThemeStyleboxOverride("hover", ButtonBox(new Color(1, 1, 1, 0.10f), Colors.Transparent));
        button.AddThemeStyleboxOverride("pressed", ButtonBox(new Color(1, 1, 1, 0.16f), Colors.Transparent));

        // The focus ring is not decoration: on a gamepad it is the only thing
        // telling the player where they are.
        button.AddThemeStyleboxOverride("focus", ButtonBox(new Color(1, 1, 1, 0.10f), Accent));

        button.Pressed += onPressed;
        return button;
    }

    /// <summary>A labelled slider row, reporting continuous values.</summary>
    public static HSlider SliderRow(
        Control parent, string label, float min, float max, float step,
        float value, Action<float> onChanged, Func<float, string>? format = null)
    {
        var row = new VBoxContainer();
        row.AddThemeConstantOverride("separation", 2);
        parent.AddChild(row);

        var caption = new Label { Text = label };
        caption.AddThemeFontSizeOverride("font_size", 14);
        caption.AddThemeColorOverride("font_color", Dim);
        row.AddChild(caption);

        var slider = new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            Value = value,
            CustomMinimumSize = new Vector2(0, 22),
            FocusMode = Control.FocusModeEnum.All,
        };
        row.AddChild(slider);

        void Refresh(double current) =>
            caption.Text = format is null ? label : $"{label}   {format((float)current)}";

        Refresh(value);

        slider.ValueChanged += current =>
        {
            Refresh(current);
            onChanged((float)current);
        };

        return slider;
    }

    /// <summary>A labelled checkbox row.</summary>
    public static CheckBox CheckRow(Control parent, string label, bool value, Action<bool> onToggled)
    {
        var box = new CheckBox
        {
            Text = label,
            ButtonPressed = value,
            FocusMode = Control.FocusModeEnum.All,
        };

        box.AddThemeFontSizeOverride("font_size", 15);
        box.AddThemeColorOverride("font_color", Ink);
        box.AddThemeColorOverride("font_focus_color", Accent);
        box.AddThemeColorOverride("font_hover_color", Accent);

        box.Toggled += pressed => onToggled(pressed);
        parent.AddChild(box);
        return box;
    }

    /// <summary>
    /// Wires a wrapping vertical focus chain through <paramref name="controls"/>.
    /// </summary>
    /// <remarks>
    /// Godot's automatic neighbour search works from on-screen geometry and gets
    /// confused by nested containers, so the chain is stated explicitly. It
    /// wraps, because reaching the bottom of a short menu and finding the stick
    /// dead is a small papercut that shows up constantly on a pad.
    ///
    /// It deliberately does <em>not</em> focus anything. Menus are built once and
    /// shown many times, often while hidden and often two at a time — a chain
    /// that grabbed focus as a side effect of construction would fight whichever
    /// menu is actually on screen. Focusing is <see cref="Focus"/>, called when a
    /// menu opens.
    /// </remarks>
    public static void FocusChain(params Control[] controls)
    {
        if (controls.Length == 0)
            return;

        for (int i = 0; i < controls.Length; i++)
        {
            Control previous = controls[(i - 1 + controls.Length) % controls.Length];
            Control next = controls[(i + 1) % controls.Length];

            controls[i].FocusNeighborTop = controls[i].GetPathTo(previous);
            controls[i].FocusNeighborBottom = controls[i].GetPathTo(next);
            controls[i].FocusPrevious = controls[i].GetPathTo(previous);
            controls[i].FocusNext = controls[i].GetPathTo(next);
        }
    }

    /// <summary>
    /// Focuses a control once it is actually on screen.
    /// </summary>
    /// <remarks>
    /// Godot refuses to focus a hidden control, and a menu is usually shown in
    /// the same frame it is asked to take focus — before the layout pass has made
    /// it visible. Deferring to the end of the frame is what makes "open the
    /// panel and start on its first row" work on a pad.
    /// </remarks>
    public static void Focus(Control? control)
    {
        if (control is not null && GodotObject.IsInstanceValid(control))
            control.CallDeferred(Control.MethodName.GrabFocus);
    }

    private static StyleBoxFlat ButtonBox(Color background, Color border) => new()
    {
        BgColor = background,
        BorderColor = border,
        BorderWidthTop = border.A > 0 ? 1 : 0,
        BorderWidthBottom = border.A > 0 ? 1 : 0,
        BorderWidthLeft = border.A > 0 ? 1 : 0,
        BorderWidthRight = border.A > 0 ? 1 : 0,
        CornerRadiusTopLeft = 8,
        CornerRadiusTopRight = 8,
        CornerRadiusBottomLeft = 8,
        CornerRadiusBottomRight = 8,
        ContentMarginLeft = 18,
        ContentMarginRight = 18,
        ContentMarginTop = 8,
        ContentMarginBottom = 8,
    };
}
