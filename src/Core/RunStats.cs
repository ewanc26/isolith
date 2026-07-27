using System;
using System.Text.Json.Serialization;

namespace Isolith.Core;

/// <summary>
/// The result of one attempt at a course. This is the game's own record —
/// written locally on every completion, and only additionally pushed to a
/// repo if the optional sync module is signed in.
/// </summary>
public sealed class RunStats
{
    [JsonPropertyName("courseId")] public string CourseId { get; init; } = "";

    /// <summary>
    /// SHA-256 of the course JSON. Times are only comparable within a hash: edit
    /// the level, and old runs stay attached to the layout they were set on.
    /// </summary>
    [JsonPropertyName("courseHash")] public string CourseHash { get; init; } = "";

    [JsonPropertyName("timeMs")] public int TimeMs { get; set; }
    [JsonPropertyName("shardsCollected")] public int ShardsCollected { get; set; }
    [JsonPropertyName("shardsTotal")] public int ShardsTotal { get; set; }
    [JsonPropertyName("deaths")] public int Deaths { get; set; }
    [JsonPropertyName("jumps")] public int Jumps { get; set; }
    [JsonPropertyName("completed")] public bool Completed { get; set; }

    /// <summary>Sections cleared. Only meaningful for an endless run.</summary>
    [JsonPropertyName("sections")] public int Sections { get; set; }

    [JsonPropertyName("startedAt")] public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>True when every shard in the course was collected.</summary>
    [JsonIgnore] public bool FullClear => ShardsTotal > 0 && ShardsCollected >= ShardsTotal;

    /// <summary>The run time as <c>m:ss.mmm</c>.</summary>
    [JsonIgnore]
    public string TimeText => Format(TimeMs);

    /// <summary>Formats milliseconds as <c>m:ss.mmm</c>.</summary>
    public static string Format(int milliseconds)
    {
        TimeSpan span = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return $"{(int)span.TotalMinutes}:{span.Seconds:00}.{span.Milliseconds:000}";
    }

    /// <summary>
    /// Orders runs the way a player would: finished before unfinished, then
    /// more shards, then faster.
    /// </summary>
    public static int CompareForLeaderboard(RunStats a, RunStats b)
    {
        int completed = b.Completed.CompareTo(a.Completed);
        if (completed != 0)
            return completed;

        // An endless run is ranked by how far it got, not how quickly.
        int sections = b.Sections.CompareTo(a.Sections);
        if (sections != 0)
            return sections;

        int shards = b.ShardsCollected.CompareTo(a.ShardsCollected);
        if (shards != 0)
            return shards;

        return a.TimeMs.CompareTo(b.TimeMs);
    }
}
