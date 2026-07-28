using Godot;
using Isolith.Core;
using Isolith.Gameplay;

namespace Isolith.UI;

/// <summary>
/// The title screen. The game's entry point, and the only place that chooses
/// which mode a session starts in.
/// </summary>
/// <remarks>
/// Loading the game scene from here — rather than making the game the main scene
/// and overlaying a menu — means a session always starts clean. Returning to the
/// menu frees the whole game tree, so there is no state to reset and nothing to
/// leak between runs.
/// </remarks>
[GlobalClass]
public partial class MainMenu : Control
{
    private const string GameScene = "res://scenes/Main.tscn";

    /// <summary>The title screen itself, and the project's main scene.</summary>
    public const string MenuScene = "res://scenes/Menu.tscn";

    private SettingsPanel _settings = null!;
    private Control _root = null!;
    private Label _best = null!;
    private Button _settingsButton = null!;

    public override void _Ready()
    {
        // Settings have to be live before anything reads them, and the menu is
        // the first thing the player sees.
        Settings.Apply();
        GameInput.Configure();

        AnchorRight = 1;
        AnchorBottom = 1;

        Build();
    }

    public override void _Input(InputEvent @event) => GameInput.Observe(@event);

    private void Build()
    {
        AddChild(MenuKit.Scrim());

        _root = new CenterContainer { AnchorRight = 1, AnchorBottom = 1 };
        AddChild(_root);

        PanelContainer card = MenuKit.Card(430f);
        _root.AddChild(card);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        card.AddChild(column);

        column.AddChild(MenuKit.Title("ISOLITH"));
        column.AddChild(MenuKit.Caption("An isometric platformer that reads how you play."));

        var spacer = new Control { CustomMinimumSize = new Vector2(0, 12) };
        column.AddChild(spacer);

        Button endless = MenuKit.MenuButton("Endless run", () => StartGame(GameMode.Endless));
        Button ascent = MenuKit.MenuButton("The Ascent", () => StartGame(GameMode.Authored));
        Button settings = MenuKit.MenuButton("Settings", () => _settings.Open());
        Button quit = MenuKit.MenuButton("Quit", () => GetTree().Quit());

        column.AddChild(endless);
        column.AddChild(ascent);
        column.AddChild(settings);
        column.AddChild(quit);

        column.AddChild(new HSeparator());

        _best = MenuKit.Caption(BestText());
        column.AddChild(_best);

        column.AddChild(MenuKit.Caption(
            GameInput.UsingGamepad
                ? "Left stick to choose  ·  A to select"
                : "Arrows to choose  ·  Enter to select"));

        _settingsButton = settings;

        _settings = new SettingsPanel { Name = "SettingsPanel" };
        AddChild(_settings);

        _settings.Closed += () =>
        {
            _root.Visible = true;
            MenuKit.Focus(_settingsButton);
        };

        // Opening settings hides the menu behind it rather than stacking two
        // focusable layers, which would let the stick wander between them.
        settings.Pressed += () => _root.Visible = false;

        MenuKit.FocusChain(endless, ascent, settings, quit);
        MenuKit.Focus(endless);
    }

    private void StartGame(GameMode mode)
    {
        PackedScene? scene = ResourceLoader.Load<PackedScene>(GameScene);

        if (scene is null)
        {
            GD.PushError($"Isolith: could not load {GameScene}.");
            return;
        }

        var game = scene.Instantiate<GameManager>();
        game.Mode = mode;

        Swap(GetTree(), game);
    }

    /// <summary>Ends the current session and comes back to the title screen.</summary>
    /// <remarks>
    /// The counterpart to <see cref="StartGame"/>, and the reason neither needs
    /// to reset anything: the outgoing scene is freed whole, so a session cannot
    /// carry state — a paused tree, a half-torn-down endless course, a locked
    /// player — into the next one.
    /// </remarks>
    public static void ReturnToMenu(SceneTree tree)
    {
        PackedScene? scene = ResourceLoader.Load<PackedScene>(MenuScene);

        if (scene is null)
        {
            GD.PushError($"Isolith: could not load {MenuScene}.");
            return;
        }

        Swap(tree, scene.Instantiate<MainMenu>());
    }

    /// <summary>
    /// Replaces the current scene with <paramref name="next"/>, freeing the old
    /// one.
    /// </summary>
    /// <remarks>
    /// Not <see cref="SceneTree.ChangeSceneToPacked"/>, because that defers the
    /// swap to the end of the frame and gives no handle on the new root — and
    /// <see cref="GameManager.Mode"/> has to be set <em>before</em> the game's
    /// <c>_Ready</c> decides which kind of level to build.
    ///
    /// Unpausing is part of the swap: leaving a paused menu would freeze the
    /// scene it hands over to.
    /// </remarks>
    private static void Swap(SceneTree tree, Node next)
    {
        tree.Paused = false;

        Node? previous = tree.CurrentScene;

        tree.Root.AddChild(next);
        tree.CurrentScene = next;

        // Detached before it is freed: QueueFree runs at the end of the frame,
        // and until then the old scene would keep handling input alongside the
        // new one.
        if (previous is not null)
        {
            tree.Root.RemoveChild(previous);
            previous.QueueFree();
        }
    }

    private static string BestText()
    {
        RunStats? endless = RunHistory.FurthestEndless();
        RunStats? ascent = RunHistory.BestFor("ascent", string.Empty);

        if (endless is { Sections: > 0 })
            return $"Furthest endless run: {endless.Sections} sections";

        return ascent is null ? "No runs yet" : $"Best ascent: {ascent.TimeText}";
    }
}
