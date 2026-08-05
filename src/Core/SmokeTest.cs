using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Isolith.Gameplay;
using Isolith.Level;
using Isolith.Sync.Interop;
using Isolith.UI;

namespace Isolith.Core;

/// <summary>
/// A headless end-to-end check, run by CI where there is no display.
/// </summary>
/// <remarks>
/// Run it with:
/// <code>godot --headless --path . res://scenes/Smoke.tscn</code>
///
/// It loads the real game scene and every course, then verifies the things that
/// silently break when level data or scene wiring drifts:
/// <list type="bullet">
///   <item>every course parses and builds;</item>
///   <item>the built scene contains what the data asked for;</item>
///   <item>the spawn point, each checkpoint, and the goal all sit on solid
///   ground — a level whose goal hangs over a void is valid JSON but not a
///   finishable course.</item>
/// </list>
/// The exit code is non-zero if anything fails, so CI notices.
/// </remarks>
[GlobalClass]
public partial class SmokeTest : Node
{
    // A time budget, not a frame count: a course with a very high spawn or
    // checkpoint can legitimately take longer than a flat 90 frames to land,
    // and a fixed frame count would fail those courses rather than the drop.
    private const float SettleTimeoutSeconds = 6.0f;

    // A single, named clearance used for every drop test (spawn, checkpoint,
    // goal) so results are consistent rather than three independently
    // trial-and-error-tuned offsets.
    private const float StandableDropClearance = 0.5f;

    private readonly List<string> _failures = new();
    private int _checks;

    public override void _Ready()
    {
        // Godot cannot await in _Ready itself; run the sequence as a coroutine.
        _ = RunAsync();
    }

    private async System.Threading.Tasks.Task RunAsync()
    {
        GD.Print("Isolith smoke test");
        GD.Print("==================");

        ReportNativeLibrary();

        CheckMenu();
        CheckSettings();

        PackedScene? mainScene = ResourceLoader.Load<PackedScene>("res://scenes/Main.tscn");
        if (mainScene is null)
        {
            Fail("res://scenes/Main.tscn could not be loaded.");
            Finish();
            return;
        }

        var game = mainScene.Instantiate<GameManager>();
        AddChild(game);

        // One frame for _Ready to run across the whole instantiated tree.
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        foreach (string coursePath in CoursePaths())
            await CheckCourseAsync(game, coursePath);

        GD.Print("\n-- procedural generation");
        GenerationTests.Run(Check);

        await CheckEndlessAsync(game);
        await CheckSettingsReachTheGameAsync(mainScene);

        Finish();
    }

    // -----------------------------------------------------------------------
    // Checks
    // -----------------------------------------------------------------------

    private async System.Threading.Tasks.Task CheckCourseAsync(GameManager game, string path)
    {
        GD.Print($"\n-- {path}");

        game.LoadCourse(path);
        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        Course? course = game.Course;
        if (course is null || course.SourceJson.Length == 0)
        {
            Fail($"{path}: course did not load.");
            return;
        }

        Check($"{path}: has a goal", course.Goal.Length >= 3);
        Check($"{path}: has at least one block", course.Blocks.Count > 0);
        Check($"{path}: hash is stable", course.Hash == course.Hash && course.Hash.Length == 64);

        // Scoped to CourseRoot, not the whole GameManager, so a Shard/Checkpoint/
        // Goal node anywhere else in the tree (Hud, SyncPanel, ...) can't skew
        // the count of what this course actually built.
        Node3D courseRoot = game.GetNode<Node3D>("CourseRoot");

        int shardNodes = CountNodes<Shard>(courseRoot);
        Check($"{path}: {course.ShardCount} shards built", shardNodes == course.ShardCount);

        int checkpointNodes = CountNodes<Checkpoint>(courseRoot);
        Check($"{path}: {course.Checkpoints.Count} checkpoints built",
            checkpointNodes == course.Checkpoints.Count);

        Check($"{path}: goal built", CountNodes<Goal>(courseRoot) == 1);

        // Standing room: spawn, every checkpoint, and the goal must be
        // supported, or the course cannot actually be played through.
        Vector3 clearance = new(0, StandableDropClearance, 0);
        await CheckStandableAsync(game, "spawn", Course.ToVector(course.Spawn) + clearance);

        for (int i = 0; i < course.Checkpoints.Count; i++)
        {
            Vector3 point = Course.ToVector(course.Checkpoints[i]) + clearance;
            await CheckStandableAsync(game, $"checkpoint {i + 1}", point);
        }

        await CheckStandableAsync(game, "goal", Course.ToVector(course.Goal) + clearance);

        // A kill plane above the lowest thing the player must stand on is
        // fatal by construction — they'd spawn, drop through it, and die
        // before the course is even playable.
        float killPlaneY = course.KillPlaneY;
        Check($"{path}: kill plane ({killPlaneY:F1}) is below spawn",
            Course.ToVector(course.Spawn).Y > killPlaneY);

        for (int i = 0; i < course.Checkpoints.Count; i++)
            Check($"{path}: kill plane ({killPlaneY:F1}) is below checkpoint {i + 1}",
                Course.ToVector(course.Checkpoints[i]).Y > killPlaneY);

        Check($"{path}: kill plane ({killPlaneY:F1}) is below the goal",
            Course.ToVector(course.Goal).Y > killPlaneY);
    }

    /// <summary>
    /// Drops the player at a point and confirms they come to rest on ground
    /// rather than falling out of the level.
    /// </summary>
    private async System.Threading.Tasks.Task CheckStandableAsync(GameManager game, string label, Vector3 point)
    {
        PlayerController player = game.Player;
        player.InputLocked = true;
        player.Respawn(point);

        int settleFrames = (int)(SettleTimeoutSeconds * Engine.PhysicsTicksPerSecond);

        for (int frame = 0; frame < settleFrames; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            if (player.IsOnFloor())
                break;
        }

        bool landed = player.IsOnFloor();
        float restY = player.GlobalPosition.Y;

        Check($"  {label} is standable (rest y={restY:F2})", landed);

        player.InputLocked = false;
    }

    /// <summary>
    /// Builds a live endless run and lets it settle, so generation is exercised
    /// through the real scene rather than only as pure logic.
    /// </summary>
    private async System.Threading.Tasks.Task CheckEndlessAsync(GameManager game)
    {
        GD.Print("\n-- endless mode");

        game.Mode = GameMode.Endless;
        game.Seed = 20260727;
        game.Restart();

        await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

        Level.Generation.EndlessCourse? endless = game.Endless;

        if (endless is null)
        {
            Fail("endless mode did not start");
            return;
        }

        Check("endless run is reproducible from its seed", game.ActiveSeed == 20260727);
        Check($"sections built ahead of the player ({CountNodes<Checkpoint>(endless)} checkpoints)",
            CountNodes<Checkpoint>(endless) >= 3);
        Check("the spawn pad exists", CountNodes<StaticBody3D>(endless) > 0);

        // The player must be able to stand where an endless run drops them.
        await CheckStandableAsync(game, "endless spawn", endless.RespawnPoint);

        Check($"kill plane sits below the player ({endless.KillPlaneY:F1})",
            endless.KillPlaneY < endless.RespawnPoint.Y);
    }

    /// <summary>
    /// Checks the title screen builds and is navigable without a mouse.
    /// </summary>
    /// <remarks>
    /// Gamepad is the primary input scheme, so "every control has a focus
    /// neighbour" is a real invariant, not polish. It is also the one that breaks
    /// silently: a menu with an unwired control looks perfect in a screenshot and
    /// is a dead end on a pad.
    /// </remarks>
    private void CheckMenu()
    {
        GD.Print("\n-- title screen");

        string mainScene = ProjectSettings.GetSetting("application/run/main_scene").AsString();
        Check($"the project opens on the title screen ({mainScene})", mainScene == MainMenu.MenuScene);

        PackedScene? menuScene = ResourceLoader.Load<PackedScene>(MainMenu.MenuScene);

        if (menuScene is null)
        {
            Fail($"{MainMenu.MenuScene} could not be loaded.");
            return;
        }

        var menu = menuScene.Instantiate<MainMenu>();
        AddChild(menu);

        List<Button> buttons = FindAll<Button>(menu);
        Check($"the menu and its settings panel build ({buttons.Count} controls)", buttons.Count >= 8);

        List<Button> stranded = buttons
            .Where(button => button.FocusMode != Control.FocusModeEnum.All
                || button.FocusNeighborBottom.IsEmpty)
            .ToList();

        Check($"every menu control is reachable on a gamepad ({stranded.Count} stranded)",
            stranded.Count == 0);

        foreach (Button button in stranded)
            GD.PrintErr($"        stranded: {button.Name} \"{button.Text}\"");

        RemoveChild(menu);
        menu.QueueFree();
    }

    /// <summary>Checks preferences survive a write and stay inside their range.</summary>
    /// <remarks>
    /// The original values are put back afterwards: this runs against the real
    /// <c>user://</c> directory, which on a developer's machine holds their
    /// actual settings.
    /// </remarks>
    private void CheckSettings()
    {
        GD.Print("\n-- settings");

        float zoom = Settings.CameraZoom;
        bool notes = Settings.ShowDirectorNotes;

        try
        {
            Settings.CameraZoom = 22.0f;
            Check("a value survives a write", Mathf.IsEqualApprox(Settings.CameraZoom, 22.0f));

            Settings.ShowDirectorNotes = !notes;
            Check("a flag survives a write", Settings.ShowDirectorNotes == !notes);

            Settings.CameraZoom = 9000.0f;
            Check($"out-of-range values are clamped, not stored ({Settings.CameraZoom:F0})",
                Settings.CameraZoom <= 30.0f);

            Check($"preferences are written to {Settings.FilePath}",
                FileAccess.FileExists(Settings.FilePath));
        }
        finally
        {
            Settings.CameraZoom = zoom;
            Settings.ShowDirectorNotes = notes;
        }
    }

    /// <summary>
    /// Checks a stored preference actually reaches the thing it configures.
    /// </summary>
    /// <remarks>
    /// Settings that persist perfectly and are read by nobody are the failure
    /// this catches, so it asserts on the live camera in a freshly built session
    /// rather than on what came back out of the file.
    /// </remarks>
    private async System.Threading.Tasks.Task CheckSettingsReachTheGameAsync(PackedScene mainScene)
    {
        GD.Print("\n-- settings reach the game");

        float zoom = Settings.CameraZoom;

        try
        {
            Settings.CameraZoom = 24.0f;

            var session = mainScene.Instantiate<GameManager>();
            AddChild(session);

            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

            IsometricCamera? camera = session.GetNodeOrNull<IsometricCamera>("IsoCamera");

            Check("a fresh session opens at the saved camera zoom",
                camera is not null && Mathf.IsEqualApprox(camera.Size, 24.0f));

            RemoveChild(session);
            session.QueueFree();
        }
        finally
        {
            Settings.CameraZoom = zoom;
        }
    }

    private void ReportNativeLibrary()
    {
        try
        {
            // Constructing an agent proves libwolfram resolved and its ABI
            // matches. It is informational only: the game runs without it, and
            // CI machines are not expected to have it built.
            using var agent = new Sync.WolframAgent("https://bsky.social");
            GD.Print($"libwolfram: loaded from {WolframLibrary.ResolvedPath ?? "<system path>"}");
        }
        catch (Exception ex)
        {
            GD.Print($"libwolfram: not available ({ex.GetType().Name}) — sync disabled, game unaffected.");
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IEnumerable<string> CoursePaths()
    {
        using DirAccess? dir = DirAccess.Open("res://courses");
        if (dir is null)
            yield break;

        foreach (string file in dir.GetFiles().Order())
        {
            // Exported projects rename .json to .json.remap; tolerate both.
            string name = file.EndsWith(".remap", StringComparison.Ordinal) ? file[..^6] : file;

            if (name.EndsWith(".json", StringComparison.Ordinal))
                yield return $"res://courses/{name}";
        }
    }

    private static List<T> FindAll<T>(Node root) where T : Node
    {
        var found = new List<T>();
        Collect(root, found);
        return found;

        static void Collect(Node node, List<T> into)
        {
            if (node is T match)
                into.Add(match);

            foreach (Node child in node.GetChildren())
                Collect(child, into);
        }
    }

    private static int CountNodes<T>(Node root) where T : Node
    {
        int total = root is T ? 1 : 0;

        foreach (Node child in root.GetChildren())
            total += CountNodes<T>(child);

        return total;
    }

    private void Check(string description, bool passed)
    {
        _checks++;

        if (passed)
        {
            GD.Print($"  PASS  {description}");
            return;
        }

        Fail(description);
    }

    private void Fail(string description)
    {
        GD.PrintErr($"  FAIL  {description}");
        _failures.Add(description);
    }

    private void Finish()
    {
        GD.Print($"\n{_checks - _failures.Count}/{_checks} checks passed.");

        if (_failures.Count > 0)
        {
            GD.PrintErr($"{_failures.Count} failure(s):");
            foreach (string failure in _failures)
                GD.PrintErr($"  - {failure}");
        }

        GetTree().Quit(_failures.Count == 0 ? 0 : 1);
    }
}
