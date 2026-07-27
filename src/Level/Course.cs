using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace Isolith.Level;

/// <summary>What a course block does when the player touches it.</summary>
public enum BlockKind
{
    /// <summary>Plain collision geometry.</summary>
    Solid,

    /// <summary>Cosmetic variant of <see cref="Solid"/>.</summary>
    Grass,

    /// <summary>Kills on contact and sends the player back to the last checkpoint.</summary>
    Hazard,

    /// <summary>Launches the player upward on landing.</summary>
    Bounce,

    /// <summary>Falls away shortly after being stood on.</summary>
    Crumble,
}

/// <summary>A static box of level geometry.</summary>
public sealed class BlockDef
{
    [JsonPropertyName("pos")] public float[] Position { get; init; } = { 0, 0, 0 };
    [JsonPropertyName("size")] public float[] Size { get; init; } = { 4, 1, 4 };

    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BlockKind Kind { get; init; } = BlockKind.Solid;
}

/// <summary>A platform that shuttles between two points.</summary>
public sealed class MoverDef
{
    [JsonPropertyName("from")] public float[] From { get; init; } = { 0, 0, 0 };
    [JsonPropertyName("to")] public float[] To { get; init; } = { 0, 0, 0 };
    [JsonPropertyName("size")] public float[] Size { get; init; } = { 3, 0.6f, 3 };

    /// <summary>Seconds for a full there-and-back cycle.</summary>
    [JsonPropertyName("period")] public float Period { get; init; } = 4.0f;

    /// <summary>Cycle offset in the range 0–1, for staggering several movers.</summary>
    [JsonPropertyName("phase")] public float Phase { get; init; }
}

/// <summary>
/// A course, loaded from JSON under <c>res://courses/</c>. Levels are data, not
/// scenes, so they can be authored, diffed, and hashed as plain text — the hash
/// is what a synced run record refers to.
/// </summary>
public sealed class Course
{
    [JsonPropertyName("id")] public string Id { get; init; } = "untitled";
    [JsonPropertyName("name")] public string Name { get; init; } = "Untitled";
    [JsonPropertyName("author")] public string Author { get; init; } = "";
    [JsonPropertyName("spawn")] public float[] Spawn { get; init; } = { 0, 2, 0 };

    /// <summary>Falling below this Y counts as a death.</summary>
    [JsonPropertyName("killPlaneY")] public float KillPlaneY { get; init; } = -20f;

    [JsonPropertyName("blocks")] public List<BlockDef> Blocks { get; init; } = new();
    [JsonPropertyName("movers")] public List<MoverDef> Movers { get; init; } = new();

    /// <summary>Collectible positions.</summary>
    [JsonPropertyName("shards")] public List<float[]> Shards { get; init; } = new();

    [JsonPropertyName("checkpoints")] public List<float[]> Checkpoints { get; init; } = new();
    [JsonPropertyName("goal")] public float[] Goal { get; init; } = { 0, 2, 0 };

    /// <summary>Total collectibles in the course.</summary>
    [JsonIgnore] public int ShardCount => Shards.Count;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Loads a course from a Godot resource path. Uses <see cref="FileAccess"/>
    /// rather than <c>System.IO</c> so it also works from inside an exported
    /// .pck, where course files are not real files on disk.
    /// </summary>
    public static Course Load(string resourcePath)
    {
        using FileAccess file = FileAccess.Open(resourcePath, FileAccess.ModeFlags.Read);
        if (file is null)
            throw new InvalidOperationException(
                $"Could not open course '{resourcePath}': {FileAccess.GetOpenError()}");

        string json = file.GetAsText();
        return Parse(json, resourcePath);
    }

    /// <summary>Parses course JSON, with the source path used only for error messages.</summary>
    public static Course Parse(string json, string source = "<memory>")
    {
        Course? course;
        try
        {
            course = JsonSerializer.Deserialize<Course>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Course '{source}' is not valid JSON: {ex.Message}", ex);
        }

        if (course is null)
            throw new InvalidOperationException($"Course '{source}' deserialised to null.");

        course.SourceJson = json;
        return course;
    }

    /// <summary>The exact JSON this course was loaded from, kept for hashing.</summary>
    [JsonIgnore] public string SourceJson { get; private set; } = string.Empty;

    /// <summary>
    /// Stable SHA-256 of the course source, so a synced run can state precisely
    /// which version of a level it was set on. Editing a course changes the hash
    /// and therefore separates its times from the old layout's.
    /// </summary>
    [JsonIgnore]
    public string Hash
    {
        get
        {
            if (_hash is not null)
                return _hash;

            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(SourceJson);
            _hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
            return _hash;
        }
    }

    private string? _hash;

    internal static Vector3 ToVector(float[] values) =>
        values.Length >= 3 ? new Vector3(values[0], values[1], values[2]) : Vector3.Zero;
}
