using Godot;

namespace Isolith.Level;

/// <summary>
/// The game's entire look, in one place.
/// </summary>
/// <remarks>
/// Two kinds of surface, treated deliberately differently:
///
/// <b>Neutral geometry</b> (solid, grass, crumbling platforms) uses CC0 PBR
/// materials from ambientCG, triplanar-mapped so a box of any size is textured
/// without authored UVs, and tinted toward the palette colours below so the
/// scene still reads as one place.
///
/// <b>Gameplay signals</b> (hazards, bounce pads, the goal, shards,
/// checkpoints) stay flat and emissive. A player needs to identify these at a
/// glance from a fixed isometric distance, and photographic detail actively
/// hurts that — texture is noise on something whose whole job is to be legible.
///
/// If the third-party assets are absent (a clone that has not run
/// <c>tools/fetch_assets.py</c>), every material falls back to its flat colour
/// and the game looks plainer but runs identically.
/// </remarks>
public static class Palette
{
    // Base hues. Deliberately desaturated so the accent colours read clearly.
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

    private const string TextureRoot = "res://assets/thirdparty/ambientcg";

    /// <summary>
    /// World-space texture repeat, in metres. One repeat every four metres keeps
    /// the grain readable at the camera's fixed orthographic distance without
    /// turning into visible tiling.
    /// </summary>
    private const float TriplanarScale = 0.25f;

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

    // --- Neutral geometry: textured where the assets are available -----------

    public static StandardMaterial3D Solid =>
        _stone ??= Textured("Rock030", Stone, roughness: 0.85f);

    public static StandardMaterial3D Grass =>
        _moss ??= Textured("Grass004", Moss, roughness: 0.95f);

    public static StandardMaterial3D Fragile =>
        _crumble ??= Textured("Concrete034", Crumble, roughness: 0.7f);

    // --- Gameplay signals: always flat and emissive ---------------------------

    public static StandardMaterial3D Spike => _hazard ??= Emissive(Hazard, energy: 0.5f);
    public static StandardMaterial3D Springboard => _bounce ??= Emissive(Bounce, energy: 0.7f);
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

    /// <summary>True when the third-party texture packs are present.</summary>
    public static bool TexturesAvailable =>
        ResourceLoader.Exists($"{TextureRoot}/Rock030/Rock030_1K-JPG_Color.jpg");

    // -----------------------------------------------------------------------
    // Construction
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a triplanar PBR material from an ambientCG pack, falling back to a
    /// flat colour if the pack is not present.
    /// </summary>
    private static StandardMaterial3D Textured(string assetId, Color tint, float roughness)
    {
        StandardMaterial3D material = Surface(tint, roughness);

        Texture2D? albedo = Map(assetId, "Color");
        if (albedo is null)
            return material;

        material.AlbedoTexture = albedo;

        // Tinting rather than replacing: the pack supplies detail, the palette
        // supplies identity. Pushed toward white so the photographic albedo is
        // not crushed.
        material.AlbedoColor = tint.Lerp(Colors.White, 0.55f);

        if (Map(assetId, "NormalGL") is { } normal)
        {
            material.NormalEnabled = true;
            material.NormalTexture = normal;
            material.NormalScale = 0.8f;
        }

        if (Map(assetId, "Roughness") is { } roughnessMap)
        {
            material.RoughnessTexture = roughnessMap;
            material.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Red;
            material.Roughness = 1.0f; // the map is the roughness; do not scale it down
        }

        if (Map(assetId, "AmbientOcclusion") is { } occlusion)
        {
            material.AOEnabled = true;
            material.AOTexture = occlusion;
            material.AOTextureChannel = BaseMaterial3D.TextureChannel.Red;
            material.AOLightAffect = 0.6f;
        }

        // Triplanar: course geometry is boxes of arbitrary size with no authored
        // UVs, so projecting from world space is the only mapping that keeps
        // texel density constant across a 3 m platform and a 12 m wall.
        material.Uv1Triplanar = true;
        material.Uv1WorldTriplanar = true;
        material.Uv1Scale = new Vector3(TriplanarScale, TriplanarScale, TriplanarScale);

        return material;
    }

    private static Texture2D? Map(string assetId, string suffix)
    {
        string path = $"{TextureRoot}/{assetId}/{assetId}_1K-JPG_{suffix}.jpg";

        // Not every pack ships every map — Concrete034 has no ambient occlusion.
        return ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
    }

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
