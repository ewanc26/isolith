using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Isolith.Core;
using Isolith.Gameplay;
using Isolith.Level;
using Isolith.Sync;

namespace Isolith.UI;

/// <summary>
/// The optional sync panel, opened with F1.
/// </summary>
/// <remarks>
/// Everything here is opt-in. The panel starts hidden, the game never prompts
/// for it, and closing it leaves a fully working single-player game with its
/// own local history. Sign-in state is deliberately not persisted: the app
/// password is used for one <c>createSession</c> call and then dropped, so
/// nothing sensitive is ever written to disk.
/// </remarks>
[GlobalClass]
public partial class SyncPanel : CanvasLayer
{
    private const string AutoSyncSetting = "user://sync.cfg";

    private GameManager _game = null!;
    private Hud _hud = null!;
    private SyncService _sync = null!;

    private PanelContainer _panel = null!;
    private LineEdit _identifier = null!;
    private LineEdit _password = null!;
    private LineEdit _service = null!;
    private Button _signIn = null!;
    private Button _signOut = null!;
    private Button _publishNow = null!;
    private CheckBox _autoSync = null!;
    private Label _status = null!;
    private Label _history = null!;

    private RunStats? _lastRun;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 2;

        _game = GetParent<GameManager>()
            ?? throw new InvalidOperationException("Isolith: SyncPanel must be a child of GameManager.");
        _hud = _game.GetNode<Hud>("Hud");

        _sync = new SyncService { Name = "SyncService" };
        AddChild(_sync);

        Build();

        _sync.StateChanged += OnSyncStateChanged;
        _sync.RunPublished += OnRunPublished;
        _sync.RunsFetched += OnRunsFetched;
        _game.RunCompleted += OnRunCompleted;

        _autoSync.ButtonPressed = LoadAutoSyncPreference();
        _panel.Visible = false;
        RefreshStatus();
    }

    public override void _ExitTree()
    {
        _game.RunCompleted -= OnRunCompleted;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed(GameInput.ToggleSync))
            return;

        Toggle();
        GetViewport().SetInputAsHandled();
    }

    /// <summary>Shows or hides the panel, suppressing gameplay input while open.</summary>
    public void Toggle()
    {
        _panel.Visible = !_panel.Visible;
        _game.SetUiFocus(_panel.Visible);

        if (_panel.Visible && !_sync.IsSignedIn)
            _identifier.GrabFocus();
    }

    // -----------------------------------------------------------------------
    // Sync events
    // -----------------------------------------------------------------------

    private void OnRunCompleted(RunStats run)
    {
        _lastRun = run;

        if (_autoSync.ButtonPressed && _sync.IsSignedIn)
            _sync.PublishRun(run);

        RefreshStatus();
    }

    private void OnSyncStateChanged(SyncState state)
    {
        RefreshStatus();

        if (state == SyncState.SignedIn)
        {
            // The password has done its job; keep it out of memory and off screen.
            _password.Text = string.Empty;
        }
    }

    private void OnRunPublished(RecordRef reference)
    {
        _status.Text = $"Published to your repo\n{reference.Uri}";
        _sync.FetchRuns();
    }

    private void OnRunsFetched(List<RunStats> runs)
    {
        Course? course = _game.Course;

        List<RunStats> relevant = course is null
            ? runs
            : runs.Where(run => run.CourseId == course.Id).ToList();

        if (relevant.Count == 0)
        {
            _history.Text = "No synced runs for this course yet.";
            return;
        }

        IEnumerable<string> lines = relevant
            .Order(Comparer<RunStats>.Create(RunStats.CompareForLeaderboard))
            .Take(5)
            .Select((run, index) =>
                $"{index + 1}.  {run.TimeText}   {run.ShardsCollected}/{run.ShardsTotal} shards   {run.Deaths} deaths");

        _history.Text = string.Join('\n', lines);
    }

    // -----------------------------------------------------------------------
    // Actions
    // -----------------------------------------------------------------------

    private void OnSignInPressed()
    {
        if (string.IsNullOrWhiteSpace(_identifier.Text) || string.IsNullOrEmpty(_password.Text))
        {
            _status.Text = "Enter your handle and an app password.";
            return;
        }

        _sync.SignIn(_identifier.Text, _password.Text, _service.Text);
    }

    private void OnPublishNowPressed()
    {
        if (_lastRun is null)
        {
            _status.Text = "Finish a course first — there is nothing to publish yet.";
            return;
        }

        _sync.PublishRun(_lastRun);
    }

    private void OnAutoSyncToggled(bool enabled)
    {
        using FileAccess file = FileAccess.Open(AutoSyncSetting, FileAccess.ModeFlags.Write);
        file?.StoreString(enabled ? "1" : "0");
    }

    private static bool LoadAutoSyncPreference()
    {
        if (!FileAccess.FileExists(AutoSyncSetting))
            return false;

        using FileAccess file = FileAccess.Open(AutoSyncSetting, FileAccess.ModeFlags.Read);
        return file?.GetAsText().Trim() == "1";
    }

    private void RefreshStatus()
    {
        bool signedIn = _sync.IsSignedIn;

        _signIn.Visible = !signedIn;
        _identifier.Editable = !signedIn;
        _password.Visible = !signedIn;
        _service.Editable = !signedIn;
        _signOut.Visible = signedIn;
        _publishNow.Disabled = !signedIn || _lastRun is null;

        _status.Text = _sync.State switch
        {
            SyncState.SignedOut => "Not signed in. Runs are still saved locally.",
            SyncState.Connecting => "Connecting…",
            SyncState.Working => "Working…",
            SyncState.SignedIn => $"Signed in as {_sync.Handle}",
            SyncState.Failed => _sync.LastError,
            _ => string.Empty,
        };

        _hud.SetSyncStatus(signedIn, _sync.Handle);
    }

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    private void Build()
    {
        var centre = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(centre);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(520, 0) };
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(Palette.Sky, 0.96f),
            BorderColor = new Color(1, 1, 1, 0.14f),
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ContentMarginLeft = 28,
            ContentMarginRight = 28,
            ContentMarginTop = 24,
            ContentMarginBottom = 24,
        });
        centre.AddChild(_panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(column);

        column.AddChild(Title("Sync run stats"));
        column.AddChild(Caption(
            "Optional. Copies completed runs into your own AT Protocol repo through " +
            "libwolfram. The game keeps its own local history either way."));

        _identifier = Field(column, "Handle or DID", "you.example.com");
        _password = Field(column, "App password", "xxxx-xxxx-xxxx-xxxx");
        _password.Secret = true;
        _service = Field(column, "PDS or entryway", SyncService.DefaultService);
        _service.Text = SyncService.DefaultService;

        column.AddChild(Caption(
            "Use an app password, not your account password. It is sent once to create " +
            "a session and is never written to disk."));

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        column.AddChild(buttons);

        _signIn = new Button { Text = "Sign in" };
        _signIn.Pressed += OnSignInPressed;
        buttons.AddChild(_signIn);

        _signOut = new Button { Text = "Sign out" };
        _signOut.Pressed += _sync.SignOut;
        buttons.AddChild(_signOut);

        _publishNow = new Button { Text = "Publish last run" };
        _publishNow.Pressed += OnPublishNowPressed;
        buttons.AddChild(_publishNow);

        _autoSync = new CheckBox { Text = "Publish every completed run automatically" };
        _autoSync.Toggled += OnAutoSyncToggled;
        column.AddChild(_autoSync);

        column.AddChild(new HSeparator());

        _status = Caption("");
        column.AddChild(_status);

        column.AddChild(Caption("Your synced times for this course:"));
        _history = Caption("Sign in and publish a run to see them here.");
        column.AddChild(_history);

        column.AddChild(Caption("Back (or F1) closes this panel."));
    }

    private static Label Title(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 24);
        return label;
    }

    private static Label Caption(string text)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(460, 0),
        };
        label.AddThemeFontSizeOverride("font_size", 13);
        label.Modulate = new Color(1, 1, 1, 0.72f);
        return label;
    }

    private static LineEdit Field(Control parent, string label, string placeholder)
    {
        parent.AddChild(Caption(label));

        var edit = new LineEdit { PlaceholderText = placeholder };
        parent.AddChild(edit);
        return edit;
    }
}
