using System;
using Godot;
using Isolith.Core;
using Isolith.Gameplay;
using Isolith.Level;

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

    private PanelContainer _completePanel = null!;
    private Label _completeTitle = null!;
    private Label _completeDetail = null!;
    private Label _completeHint = null!;

    private PanelContainer _pausePanel = null!;
    private Label _pauseHint = null!;
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
        RefreshControlHints();

        _game.StatsChanged += OnStatsChanged;
        _game.StateChanged += OnStateChanged;
        _game.RunCompleted += OnRunCompleted;
    }

    public override void _ExitTree()
    {
        _game.StatsChanged -= OnStatsChanged;
        _game.StateChanged -= OnStateChanged;
        _game.RunCompleted -= OnRunCompleted;
    }

    public override void _Input(InputEvent @event)
    {
        GameInput.Observe(@event);

        // Hints follow whichever device the player just touched, so a pad user
        // never reads keyboard instructions.
        if (GameInput.UsingGamepad != _lastUsingGamepad)
            RefreshControlHints();
    }

    /// <summary>Rewrites every on-screen hint for the active input device.</summary>
    private void RefreshControlHints()
    {
        _lastUsingGamepad = GameInput.UsingGamepad;

        if (_lastUsingGamepad)
        {
            _controls.Text = "Left stick move   ·   A jump   ·   LB/RB turn view   ·   Y restart";
            _pauseHint.Text = "Start to resume   ·   Y to restart";
            _completeHint.Text = "Y to run it again   ·   Back for sync";
        }
        else
        {
            _controls.Text = "WASD move   ·   Space jump   ·   Q/E turn view   ·   R restart";
            _pauseHint.Text = "Esc to resume   ·   R to restart";
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
        _shards = Readout("Shards 0 / 0");
        _deaths = Readout("Deaths 0");
        _best = Readout("");

        column.AddChild(_time);
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

        var hint = Readout("");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        hint.Modulate = new Color(1, 1, 1, 0.6f);
        _pauseHint = hint;

        column.AddChild(title);
        column.AddChild(hint);

        _pausePanel.Visible = false;
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

        _syncLine = Readout("");
        _syncLine.Modulate = new Color(1, 1, 1, 0.45f);

        row.AddChild(_controls);
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
    }

    private void OnStateChanged(GameState state)
    {
        _completePanel.Visible = state == GameState.Complete;
        _pausePanel.Visible = state == GameState.Paused;

        if (state == GameState.Playing)
            ShowPersonalBest();
    }

    private void OnRunCompleted(RunStats run)
    {
        string clear = run.FullClear ? "all shards" : $"{run.ShardsCollected}/{run.ShardsTotal} shards";
        _completeDetail.Text = $"{run.TimeText}   ·   {clear}   ·   {run.Deaths} deaths";

        RunStats? best = RunHistory.BestFor(run.CourseId, run.CourseHash);
        if (best is not null && best.TimeMs < run.TimeMs)
            _completeTitle.Text = "Course complete";
        else
            _completeTitle.Text = "Course complete — new best";
    }

    private void ShowPersonalBest()
    {
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
