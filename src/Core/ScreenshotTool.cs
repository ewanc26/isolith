using Godot;
using Isolith.Gameplay;
using Isolith.UI;

namespace Isolith.Core;

/// <summary>
/// Captures stills of the running game, for the README and for eyeballing a
/// visual change without playing through to it.
/// </summary>
/// <remarks>
/// Needs a real renderer, so unlike <see cref="SmokeTest"/> this cannot run
/// headless:
/// <code>godot --path . res://scenes/Screenshot.tscn</code>
/// Frames land in <c>docs/shots/</c>.
/// </remarks>
[GlobalClass]
public partial class ScreenshotTool : Node
{
    [Export] public int SettleFrames { get; set; } = 90;
    [Export] public string OutputDirectory { get; set; } = "res://docs/shots";

    public override void _Ready()
    {
        // The pause shot freezes the tree, and a pausable node cannot drive a
        // coroutine that has to outlive the pause.
        ProcessMode = ProcessModeEnum.Always;

        _ = CaptureAsync();
    }

    private async System.Threading.Tasks.Task CaptureAsync()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutputDirectory));

        await CaptureMenuAsync();

        PackedScene scene = ResourceLoader.Load<PackedScene>("res://scenes/Main.tscn");
        var game = scene.Instantiate<GameManager>();
        game.Seed = 20260727;
        AddChild(game);

        // Let physics settle so the player is standing, not mid-drop.
        for (int frame = 0; frame < SettleFrames; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        await Capture("endless");

        // A second angle: the camera's 90° rotation is the feature most worth
        // seeing in a still.
        game.GetNode<IsometricCamera>("IsoCamera").ViewSize = 24.0f;
        for (int frame = 0; frame < 30; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        await Capture("endless-wide");

        game.TogglePause();

        for (int frame = 0; frame < 20; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        await Capture("paused");

        GetTree().Quit();
    }

    /// <summary>Shoots the title screen on its own, before any session exists.</summary>
    private async System.Threading.Tasks.Task CaptureMenuAsync()
    {
        PackedScene? scene = ResourceLoader.Load<PackedScene>(MainMenu.MenuScene);

        if (scene is null)
        {
            GD.PushError($"Isolith: could not load {MainMenu.MenuScene}.");
            return;
        }

        var menu = scene.Instantiate<MainMenu>();
        AddChild(menu);

        // Long enough for the layout pass and the deferred focus grab, so the
        // focus ring is in the shot — it is half the point of the menu.
        for (int frame = 0; frame < 20; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        await Capture("menu");

        // Driven through the real button rather than by reaching into the menu,
        // so the shot proves the wiring and not just the widgets.
        FindButton(menu, "Settings")?.EmitSignal(BaseButton.SignalName.Pressed);

        for (int frame = 0; frame < 20; frame++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        await Capture("settings");

        RemoveChild(menu);
        menu.QueueFree();
    }

    private static Button? FindButton(Node root, string text)
    {
        if (root is Button button && button.Text == text)
            return button;

        foreach (Node child in root.GetChildren())
        {
            if (FindButton(child, text) is { } found)
                return found;
        }

        return null;
    }

    private async System.Threading.Tasks.Task Capture(string name)
    {
        // The viewport texture is only valid once the frame has been drawn.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        Image image = GetViewport().GetTexture().GetImage();
        string path = $"{OutputDirectory}/{name}.png";

        Error error = image.SavePng(ProjectSettings.GlobalizePath(path));
        GD.Print(error == Error.Ok ? $"wrote {path}" : $"failed to write {path}: {error}");
    }
}
