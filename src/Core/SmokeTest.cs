using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Isolith.Gameplay;
using Isolith.Level;
using Isolith.Sync.Interop;

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
    private const int SettleFrames = 90;

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

        int shardNodes = CountNodes<Shard>(game);
        Check($"{path}: {course.ShardCount} shards built", shardNodes == course.ShardCount);

        int checkpointNodes = CountNodes<Checkpoint>(game);
        Check($"{path}: {course.Checkpoints.Count} checkpoints built",
            checkpointNodes == course.Checkpoints.Count);

        Check($"{path}: goal built", CountNodes<Goal>(game) == 1);

        // Standing room: spawn, every checkpoint, and the goal must be
        // supported, or the course cannot actually be played through.
        await CheckStandableAsync(game, "spawn", Course.ToVector(course.Spawn));

        for (int i = 0; i < course.Checkpoints.Count; i++)
        {
            Vector3 point = Course.ToVector(course.Checkpoints[i]) + new Vector3(0, 1.2f, 0);
            await CheckStandableAsync(game, $"checkpoint {i + 1}", point);
        }

        await CheckStandableAsync(game, "goal", Course.ToVector(course.Goal) + new Vector3(0, 1.0f, 0));
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

        for (int frame = 0; frame < SettleFrames; frame++)
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
