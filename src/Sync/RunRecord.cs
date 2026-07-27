using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Isolith.Core;

namespace Isolith.Sync;

/// <summary>
/// Translates between the game's <see cref="RunStats"/> and the
/// <c>uk.ewancroft.isolith.run</c> lexicon.
/// </summary>
/// <remarks>
/// Kept deliberately separate from <see cref="RunStats"/>: the game's own model
/// should be free to change without breaking a published record schema, and the
/// lexicon should be readable by anything on the network, not just this game.
/// The schema itself lives in <c>lexicons/uk/ewancroft/isolith/run.json</c>.
/// </remarks>
public static class RunRecord
{
    /// <summary>The collection runs are written to.</summary>
    public const string Collection = "uk.ewancroft.isolith.run";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    /// <summary>Builds the JSON body for a run record.</summary>
    public static string ToJson(RunStats run)
    {
        var record = new JsonObject
        {
            ["$type"] = Collection,
            ["courseId"] = run.CourseId,
            ["courseHash"] = run.CourseHash,
            ["timeMs"] = run.TimeMs,
            ["completed"] = run.Completed,
            ["deaths"] = run.Deaths,
            ["jumps"] = run.Jumps,
            ["shards"] = new JsonObject
            {
                ["collected"] = run.ShardsCollected,
                ["total"] = run.ShardsTotal,
            },
            // RFC 3339 UTC, which is what the lexicon's `datetime` format wants.
            ["createdAt"] = run.StartedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
        };

        return record.ToJsonString(Options);
    }

    /// <summary>
    /// Reads runs out of a <c>com.atproto.repo.listRecords</c> response body.
    /// Records that don't parse are skipped rather than failing the whole list —
    /// a repo can legitimately contain records written by another version.
    /// </summary>
    public static List<RunStats> ParseListRecords(string responseJson)
    {
        var runs = new List<RunStats>();

        if (string.IsNullOrWhiteSpace(responseJson))
            return runs;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(responseJson);
        }
        catch (JsonException)
        {
            return runs;
        }

        if (root?["records"] is not JsonArray records)
            return runs;

        foreach (JsonNode? entry in records)
        {
            if (entry?["value"] is not JsonObject value)
                continue;

            RunStats? run = FromRecord(value);
            if (run is not null)
                runs.Add(run);
        }

        return runs;
    }

    private static RunStats? FromRecord(JsonObject value)
    {
        try
        {
            JsonNode? shards = value["shards"];

            return new RunStats
            {
                CourseId = value["courseId"]?.GetValue<string>() ?? "",
                CourseHash = value["courseHash"]?.GetValue<string>() ?? "",
                TimeMs = value["timeMs"]?.GetValue<int>() ?? 0,
                Completed = value["completed"]?.GetValue<bool>() ?? false,
                Deaths = value["deaths"]?.GetValue<int>() ?? 0,
                Jumps = value["jumps"]?.GetValue<int>() ?? 0,
                ShardsCollected = shards?["collected"]?.GetValue<int>() ?? 0,
                ShardsTotal = shards?["total"]?.GetValue<int>() ?? 0,
                StartedAt = ParseTimestamp(value["createdAt"]?.GetValue<string>()),
            };
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            return null;
        }
    }

    private static DateTimeOffset ParseTimestamp(string? text) =>
        DateTimeOffset.TryParse(text, out DateTimeOffset parsed) ? parsed : DateTimeOffset.MinValue;
}
