using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace Isolith.Core;

/// <summary>
/// Local, on-disk record of every completed run.
/// </summary>
/// <remarks>
/// This is the authoritative store: the game is fully playable, and keeps all
/// of its history, with no account and no network. Repo sync is an extra copy
/// of the same data, never a prerequisite for it.
/// </remarks>
public static class RunHistory
{
    private const string SavePath = "user://runs.json";
    private const int MaxRuns = 500;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Appends a run and persists the history, newest first.</summary>
    public static void Record(RunStats run)
    {
        List<RunStats> runs = LoadAll();
        runs.Insert(0, run);

        if (runs.Count > MaxRuns)
            runs.RemoveRange(MaxRuns, runs.Count - MaxRuns);

        Save(runs);
    }

    /// <summary>All stored runs, newest first. Never throws; a broken file reads as empty.</summary>
    public static List<RunStats> LoadAll()
    {
        if (!FileAccess.FileExists(SavePath))
            return new List<RunStats>();

        using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            GD.PushWarning($"Isolith: could not read run history: {FileAccess.GetOpenError()}");
            return new List<RunStats>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<RunStats>>(file.GetAsText()) ?? new List<RunStats>();
        }
        catch (JsonException ex)
        {
            // A corrupt history should never stop someone from playing.
            GD.PushWarning($"Isolith: run history is unreadable, starting fresh ({ex.Message}).");
            return new List<RunStats>();
        }
    }

    /// <summary>The best run for a specific version of a course, or <c>null</c>.</summary>
    public static RunStats? BestFor(string courseId, string courseHash)
    {
        return LoadAll()
            .Where(run => run.Completed && run.CourseId == courseId && run.CourseHash == courseHash)
            .Order(Comparer<RunStats>.Create(RunStats.CompareForLeaderboard))
            .FirstOrDefault();
    }

    private static void Save(List<RunStats> runs)
    {
        using FileAccess file = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            GD.PushWarning($"Isolith: could not write run history: {FileAccess.GetOpenError()}");
            return;
        }

        file.StoreString(JsonSerializer.Serialize(runs, Options));
    }
}
