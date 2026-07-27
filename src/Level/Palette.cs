using Godot;

namespace Isolith.Level;

/// <summary>
/// The game's entire look, in one place. Every material is built in code from
/// the colours below — there are no imported texture or material assets, which
/// keeps the visual style consistent and the repository free of third-party art.
/// </summary>
public static class Palette
{
    // Base hues. Deliberately desaturated so the accent colours (shards, goal,
    // hazards) read clearly against the level geometry.
    public static readonly Color Stone = Color.FromHtml("#6d7794");
    public static readonly Color StoneEdge = Color.FromHtml("#404a63");
    public static readonly Color Moss = Color.FromHtml("#78a06a");
    public static readonly Color Hazard = Color.FromHtml("#d4576b");
    public static readonly Color Bounce = Color.FromHtml("#4fc3d9");
    public static readonly Color Crumble = Color.FromHtml("#c39a5c");
    public static readonly Color Shard = Color.FromHtml("#ffd479");
    public static readonly Color Goal = Color.FromHtml("#9d7cf5");
    public static readonly Color Checkpoint = Color.FromHtml("#57d49a");
    public static readonly Color PlayerBody = Color.FromHtml("#f2f0eb");
    public static readonly Color PlayerTrim = Color.FromHtml("#ff8f5e");
    public static readonly Color Sky = Color.FromHtml("#1b2033");
    public static readonly Color Horizon = Color.FromHtml("#3a4666");

    private static StandardMaterial3D? _stone;
    private static StandardMaterial3D? _moss;
    private static StandardMaterial3D? _hazard;
    private static StandardMaterial3D? _bounce;
    private static StandardMaterial3D? _crumble;
    private static StandardMaterial3D? _shard;
    private static StandardMaterial3D? _goal;
    private static StandardMaterial3D? _checkpointOff;
    private static StandardMaterial3D? _checkpointOn;
    private static StandardMaterial3D? _playerBody;
    private static StandardMaterial3D? _playerTrim;

    public static StandardMaterial3D Solid => _stone ??= Surface(Stone, roughness: 0.85f);
    public static StandardMaterial3D Grass => _moss ??= Surface(Moss, roughness: 0.95f);
    public static StandardMaterial3D Spike => _hazard ??= Emissive(Hazard, energy: 0.5f);
    public static StandardMaterial3D Springboard => _bounce ??= Emissive(Bounce, energy: 0.7f);
    public static StandardMaterial3D Fragile => _crumble ??= Surface(Crumble, roughness: 0.7f);
    public static StandardMaterial3D Collectible => _shard ??= Emissive(Shard, energy: 1.4f);
    public static StandardMaterial3D Finish => _goal ??= Emissive(Goal, energy: 1.1f);
    public static StandardMaterial3D CheckpointIdle => _checkpointOff ??= Surface(StoneEdge, roughness: 0.6f);
    public static StandardMaterial3D CheckpointLit => _checkpointOn ??= Emissive(Checkpoint, energy: 1.2f);
    public static StandardMaterial3D Body => _playerBody ??= Surface(PlayerBody, roughness: 0.55f);
    public static StandardMaterial3D Trim => _playerTrim ??= Emissive(PlayerTrim, energy: 0.6f);

    /// <summary>Material for a course block of the given kind.</summary>
    public static StandardMaterial3D ForBlock(BlockKind kind) => kind switch
    {
        BlockKind.Grass => Grass,
        BlockKind.Hazard => Spike,
        BlockKind.Bounce => Springboard,
        BlockKind.Crumble => Fragile,
        _ => Solid,
    };

    private static StandardMaterial3D Surface(Color albedo, float roughness) => new()
    {
        AlbedoColor = albedo,
        Roughness = roughness,
        Metallic = 0.0f,
        SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx,
    };

    private static StandardMaterial3D Emissive(Color albedo, float energy)
    {
        StandardMaterial3D material = Surface(albedo, roughness: 0.4f);
        material.EmissionEnabled = true;
        material.Emission = albedo;
        material.EmissionEnergyMultiplier = energy;
        return material;
    }
}
