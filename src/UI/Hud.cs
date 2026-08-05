using System;
using Godot;
using Isolith.Core;
using Isolith.Gameplay;
using Isolith.Level;
using Isolith.Level.Generation;

namespace Isolith.UI;

/// <summary>
/// The in-game overlay: live run stats, the pause and completion panels, and a
/// one-line sync status.
/// </summary>
/// <remarks>
/// Built in code rather than as a <c>.tscn</c>. The UI is plain text on flat
/// panels, so describing it here keeps the layout reviewable in a diff and
/// avoids shipping a binary theme resource.
/// </remarks>
[GlobalClass]
public partial class Hud : CanvasLayer
{
    private GameManager _game = null!;

    private Label _time = null!;
    private Label _shards = null!;
    private Label _deaths = null!;
    private Label _best = null!;
    private Label _syncLine = null!;
    private Label _sections = null!;
    private Label _director = null!;
    private float _directorFade;

    private PanelContainer _completePanel = null!;
    private Label _completeTitle = null!;
    private Label _completeDetail = null!;
    private Label _completeHint = null!;

    private PanelContainer _pausePanel = null!;
    private Label _pauseHint = null!;
    private Button _resumeButton = null!;
    private SettingsPanel _settings = null!;
    private ColorRect _modalScrim = null!;
    private Label _controls = null!;
    private bool _lastUsingGamepad;
    private bool _syncSignedIn;
    private string _syncHandle = string.Empty;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        _game = GetParent<GameManager>()
            ?? throw new InvalidOperationException("Isolith: Hud must be a child of GameManager.");

        BuildStatsCorner();
        BuildCompletePanel();
        BuildPausePanel();
        BuildFooter();
        BuildSettings();
        RefreshControlHints();

        _game.StatsChanged += OnStatsChanged;
        _game.StateChanged += OnStateChanged;
        _game.RunCompleted += OnRunCompleted;
        _game.SectionCompleted += OnSectionCompleted;
    }

    public override void _ExitTree()
    {
        _game.StatsChanged -= OnStatsChanged;
        _game.StateChanged -= OnStateChanged;
        _game.RunCompleted -= OnRunCompleted;
        _game.SectionCompleted -= OnSectionCompleted;
        _settings.Closed -= CloseSettings;
    }

    public override void _Process(double delta)
    {
        if (_directorFade <= 0f)
            return;

        // The director's reasoning is shown briefly, then fades. It is
        // feedback, not a permanent readout — seeing "easing, 2 deaths" once
        // explains why the next stretch is kinder.
        _directorFade -= (float)delta;
        _director.Modulate = new Color(1, 1, 1, Mathf.Clamp(_directorFade, 0f, 1f) * 0.75f);
    }

    private void OnSectionCompleted(SectionPerformance performance, SectionSpec next)
    {
        if (_game.Endless is not { } endless || !Settings.ShowDirectorNotes)
            return;

        _director.Text = $"{endless.Director.LastReason}   ·   difficulty {next.Difficulty:F2}";
        _directorFade = 4.0f;
    }

    public override void _Input(InputEvent @event)
    {
        GameInput.Observe(@event);

        // Hints follow whichever device the player just touched, so a pad user
        // never reads keyboard instructions.
        if (GameInput.UsingGamepad != _lastUsingGamepad)
            RefreshControlHints();
    }

    /// <summary>
    /// Pause and restart, for the whole game.
    /// </summary>
    /// <remarks>
    /// Both live here rather than in <see cref="GameManager"/> because the Hud is
    /// the only part of a session that keeps running while the tree is paused
    /// (<see cref="Node.ProcessModeEnum.Always"/>). A pausable node can pause the
    /// game but can never see the input that would unpause it.
    ///
    /// Event-driven rather than polled, so a press is consumed exactly once. A
    /// polled <c>IsActionJustPressed</c> stays true for the rest of the frame,
    /// which would let the same Escape both resume the game and immediately
    /// re-pause it.
    /// </remarks>
    public override void _UnhandledInput(InputEvent @event)
    {
        // A panel with text fields owns the keyboard while it is open; the
        // settings panel handles its own Back before the event reaches here.
        if (_game.UiFocused)
            return;

        if (@event.IsActionPressed(GameInput.Pause))
            _game.TogglePause();
        else if (@event.IsActionPressed(GameInput.Restart))
            _game.Restart();
        else
            return;

        GetViewport().SetInputAsHandled();
    }

    /// <summary>Rewrites every on-screen hint for the active input device.</summary>
    private void RefreshControlHints()
    {
        _lastUsingGamepad = GameInput.UsingGamepad;

        if (_lastUsingGamepad)
        {
            _controls.Text = "Left stick move   ·   A jump   ·   LB/RB turn view   ·   Y restart";
            _pauseHint.Text = "Left stick to choose   ·   A to select   ·   Start to resume";
            _completeHint.Text = "Y to run it again   ·   Back for sync";
        }
        else
        {
            _controls.Text = "WASD move   ·   Space jump   ·   Q/E turn view   ·   R restart";
            _pauseHint.Text = "Arrows to choose   ·   Enter to select   ·   Esc to resume";
            _completeHint.Text = "R to run it again   ·   F1 for sync";
        }

        UpdateSyncLine();
    }

    /// <summary>
    /// Sets the one-line sync status in the corner. The Hud owns the wording so
    /// it can name the right button for the active input device.
    /// </summary>
    public void SetSyncStatus(bool signedIn, string handle)
    {
        _syncSignedIn = signedIn;
        _syncHandle = handle;
        UpdateSyncLine();
    }

    private void UpdateSyncLine()
    {
        _syncLine.Text = _syncSignedIn
            ? $"Sync on — {_syncHandle}"
            : GameInput.UsingGamepad ? "Sync off — Back" : "Sync off — F1";
    }

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    private void BuildStatsCorner()
    {
        var margin = new MarginContainer { AnchorRight = 1, AnchorBottom = 1 };
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_top", 16);
        AddChild(margin);

        var column = new VBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
        };
        column.AddThemeConstantOverride("separation", 2);
        margin.AddChild(column);

        _time = Heading("0:00.000");
        _sections = Readout("");
        _shards = Readout("Shards 0 / 0");
        _deaths = Readout("Deaths 0");
        _best = Readout("");

        column.AddChild(_time);
        column.AddChild(_sections);
        column.AddChild(_shards);
        column.AddChild(_deaths);
        column.AddChild(_best);
    }

    private void BuildCompletePanel()
    {
        _completePanel = CentrePanel(out VBoxContainer column);

        _completeTitle = Heading("Course complete");
        _completeTitle.HorizontalAlignment = HorizontalAlignment.Center;

        _completeDetail = Readout("");
        _completeDetail.HorizontalAlignment = HorizontalAlignment.Center;

        var hint = Readout("");
        _completeHint = hint;
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Modulate = new Color(1, 1, 1, 0.6f);

        column.AddChild(_completeTitle);
        column.AddChild(_completeDetail);
        column.AddChild(hint);

        _completePanel.Visible = false;
    }

    private void BuildPausePanel()
    {
        _pausePanel = CentrePanel(out VBoxContainer column);

        Label title = Heading("Paused");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(title);

        Button resume = MenuKit.MenuButton("Resume", _game.TogglePause);
        Button restart = MenuKit.MenuButton("Restart", _game.Restart);
        Button settings = MenuKit.MenuButton("Settings", OpenSettings);
        Button menu = MenuKit.MenuButton("Main menu", () => MainMenu.ReturnToMenu(GetTree()));

        column.AddChild(resume);
        column.AddChild(restart);
        column.AddChild(settings);
        column.AddChild(menu);

        var hint = Readout("");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Modulate = new Color(1, 1, 1, 0.6f);
        _pauseHint = hint;
        column.AddChild(hint);

        MenuKit.FocusChain(resume, restart, settings, menu);
        _resumeButton = resume;

        _pausePanel.Visible = false;
    }

    /// <summary>
    /// The settings panel, shared with the title screen, plus the scrim that
    /// makes it read as modal over a frozen game.
    /// </summary>
    /// <remarks>
    /// Added last so it sits above every other layer of the Hud: a
    /// <see cref="CanvasLayer"/> draws its children in tree order.
    /// </remarks>
    private void BuildSettings()
    {
        _modalScrim = MenuKit.Scrim();
        _modalScrim.Visible = false;
        AddChild(_modalScrim);

        _settings = new SettingsPanel { Name = "SettingsPanel" };
        AddChild(_settings);

        _settings.Closed += CloseSettings;
    }

    private void OpenSettings()
    {
        // One panel at a time: leaving the pause menu behind the settings card
        // would leave two focusable layers for the stick to wander between.
        _pausePanel.Visible = false;
        _modalScrim.Visible = true;

        // Suppresses the Hud's own pause and restart handling while the panel
        // is up, so Esc backs out of settings instead of unpausing underneath it.
        _game.SetUiFocus(true);
        _settings.Open();
    }

    private void CloseSettings()
    {
        _modalScrim.Visible = false;
        _game.SetUiFocus(false);

        // Settings is only reachable from the pause menu, so that is where Back
        // returns to — unless the run ended while it was open.
        _pausePanel.Visible = _game.State == GameState.Paused;

        if (_pausePanel.Visible)
            MenuKit.Focus(_resumeButton);
    }

    private void BuildFooter()
    {
        var margin = new MarginContainer { AnchorRight = 1, AnchorBottom = 1 };
        margin.AddThemeConstantOverride("margin_left", 20);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        AddChild(margin);

        var row = new HBoxContainer
        {
            SizeFlagsVertical = Control.SizeFlags.ShrinkEnd,
            Alignment = BoxContainer.AlignmentMode.Begin,
        };
        row.AddThemeConstantOverride("separation", 24);
        margin.AddChild(row);

        _controls = Readout("");
        _controls.Modulate = new Color(1, 1, 1, 0.45f);

        _director = Readout("");
        _director.Modulate = new Color(1, 1, 1, 0f);

        _syncLine = Readout("");
        _syncLine.Modulate = new Color(1, 1, 1, 0.45f);

        row.AddChild(_controls);
        row.AddChild(_director);
        row.AddChild(_syncLine);
    }

    // -----------------------------------------------------------------------
    // Updates
    // -----------------------------------------------------------------------

    private void OnStatsChanged(RunStats stats)
    {
        _time.Text = stats.TimeText;
        _shards.Text = $"Shards {stats.ShardsCollected} / {stats.ShardsTotal}";
        _deaths.Text = $"Deaths {stats.Deaths}";

        _sections.Text = _game.Mode == GameMode.Endless ? $"Section {stats.Sections + 1}" : "";
    }

    private void OnStateChanged(GameState state)
    {
        _completePanel.Visible = state == GameState.Complete;
        _pausePanel.Visible = state == GameState.Paused && !_settings.Visible;

        if (state == GameState.Paused)
        {
            MenuKit.Focus(_resumeButton);
            return;
        }

        // Focus follows the menu off screen. Left on a hidden button it would
        // keep swallowing stick input that should be driving the character.
        GetViewport().GuiReleaseFocus();

        if (state == GameState.Playing)
            ShowPersonalBest();
    }

    private void OnRunCompleted(RunStats run)
    {
        string clear = run.FullClear ? "all shards" : $"{run.ShardsCollected}/{run.ShardsTotal} shards";
        _completeDetail.Text = $"{run.TimeText}   ·   {clear}   ·   {run.Deaths} deaths";

        // RunHistory.Record(run) has already run by the time this fires, so
        // `run` is itself part of the history BestFor draws from — it can
        // never rank worse than the best it finds. CompareForLeaderboard(run,
        // best) is therefore 0 exactly when nothing in history outranks this
        // run (shards and deaths included, not just time), and positive only
        // when an older run legitimately beats it.
        RunStats? best = RunHistory.BestFor(run.CourseId, run.CourseHash);
        bool isNewBest = best is null || RunStats.CompareForLeaderboard(run, best) <= 0;
        _completeTitle.Text = isNewBest ? "Course complete — new best" : "Course complete";
    }

    private void ShowPersonalBest()
    {
        if (_game.Mode == GameMode.Endless)
        {
            // Endless runs are compared by distance, across every seed.
            RunStats? furthest = RunHistory.FurthestEndless();
            _best.Text = furthest is null || furthest.Sections == 0
                ? ""
                : $"Best {furthest.Sections} sections";
            _best.Modulate = new Color(1, 1, 1, 0.5f);
            return;
        }

        Course? course = _game.Course;
        if (course is null)
        {
            _best.Text = "";
            return;
        }

        RunStats? best = RunHistory.BestFor(course.Id, course.Hash);
        _best.Text = best is null ? "No best yet" : $"Best {best.TimeText}";
        _best.Modulate = new Color(1, 1, 1, 0.5f);
    }

    // -----------------------------------------------------------------------
    // Widgets
    // -----------------------------------------------------------------------

    private static Label Heading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 34);
        label.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.75f));
        label.AddThemeConstantOverride("outline_size", 6);
        return label;
    }

    private static Label Readout(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", new Color(1, 1, 1));
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.75f));
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }

    /// <summary>A centred, dimmed panel used for the pause and completion states.</summary>
    private PanelContainer CentrePanel(out VBoxContainer column)
    {
        var centre = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(centre);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(Palette.Sky, 0.88f),
            BorderColor = new Color(1, 1, 1, 0.12f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 44,
            ContentMarginRight = 44,
            ContentMarginTop = 28,
            ContentMarginBottom = 28,
        });
        centre.AddChild(panel);

        column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);

        // The panel itself is a child of a CenterContainer, so returning the
        // panel lets callers toggle visibility without hiding the whole layer.
        return panel;
    }
}
