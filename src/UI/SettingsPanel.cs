using System;
using System.Collections.Generic;
using Godot;
using Isolith.Core;

namespace Isolith.UI;

/// <summary>
/// The settings screen. Shared by the main menu and the pause menu, so there is
/// one implementation and one place a setting can go missing from.
/// </summary>
[GlobalClass]
public partial class SettingsPanel : CenterContainer
{
    /// <summary>Raised when the player leaves the panel.</summary>
    public event Action? Closed;

    private readonly List<Control> _focusables = new();

    private HSlider _master = null!;
    private HSlider _effects = null!;
    private CheckBox _fullscreen = null!;
    private HSlider _zoom = null!;
    private CheckBox _notes = null!;

    public override void _Ready()
    {
        AnchorRight = 1;
        AnchorBottom = 1;
        ProcessMode = ProcessModeEnum.Always;

        Build();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Back out with the same button that opened the pause menu, which is
        // what a pad user will try first.
        if (Visible && @event.IsActionPressed(GameInput.Pause))
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    /// <summary>Shows the panel and takes focus.</summary>
    public void Open()
    {
        // A setting can change outside this panel between opens — fullscreen
        // via Alt+Enter is the obvious one — so every control is refreshed
        // from the live value rather than trusting whatever it last showed.
        _master.Value = Settings.MasterVolume;
        _effects.Value = Settings.EffectsVolume;
        _fullscreen.ButtonPressed = Settings.Fullscreen;
        _zoom.Value = Settings.CameraZoom;
        _notes.ButtonPressed = Settings.ShowDirectorNotes;

        Visible = true;

        if (_focusables.Count > 0)
            MenuKit.Focus(_focusables[0]);
    }

    private void Close()
    {
        Visible = false;
        Closed?.Invoke();
    }

    private void Build()
    {
        PanelContainer card = MenuKit.Card(520f);
        AddChild(card);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 12);
        card.AddChild(column);

        column.AddChild(MenuKit.Title("Settings", 30));

        // --- Audio ---
        column.AddChild(Section("Audio"));

        _master = MenuKit.SliderRow(column, "Master volume", 0f, 1f, 0.05f,
            Settings.MasterVolume, value => Settings.MasterVolume = value, Percent);

        _effects = MenuKit.SliderRow(column, "Effects volume", 0f, 1f, 0.05f,
            Settings.EffectsVolume, value => Settings.EffectsVolume = value, Percent);

        // --- Display ---
        column.AddChild(Section("Display"));

        _fullscreen = MenuKit.CheckRow(column, "Fullscreen", Settings.Fullscreen,
            value => Settings.Fullscreen = value);

        _zoom = MenuKit.SliderRow(column, "Camera zoom", 9f, 30f, 1f,
            Settings.CameraZoom, value => Settings.CameraZoom = value,
            value => $"{value:F0} m");

        // --- Game ---
        column.AddChild(Section("Game"));

        _notes = MenuKit.CheckRow(column,
            "Explain difficulty changes in endless mode", Settings.ShowDirectorNotes,
            value => Settings.ShowDirectorNotes = value);

        column.AddChild(MenuKit.Caption(
            "Endless mode adapts to how you play. This shows what it decided and why."));

        column.AddChild(new HSeparator());

        Button reset = MenuKit.MenuButton("Reset to defaults", () =>
        {
            Settings.Reset();

            // Reflect the reset without rebuilding the whole panel.
            _master.Value = Settings.MasterVolume;
            _effects.Value = Settings.EffectsVolume;
            _zoom.Value = Settings.CameraZoom;
            _fullscreen.ButtonPressed = Settings.Fullscreen;
            _notes.ButtonPressed = Settings.ShowDirectorNotes;
        });
        column.AddChild(reset);

        Button back = MenuKit.MenuButton("Back", Close);
        column.AddChild(back);

        _focusables.AddRange(new Control[] { _master, _effects, _fullscreen, _zoom, _notes, reset, back });
        MenuKit.FocusChain(_focusables.ToArray());

        Visible = false;
    }

    private static Label Section(string text)
    {
        var label = new Label { Text = text.ToUpperInvariant() };
        label.AddThemeFontSizeOverride("font_size", 12);
        label.AddThemeColorOverride("font_color", MenuKit.Accent);
        return label;
    }

    private static string Percent(float value) => $"{value * 100:F0}%";
}
