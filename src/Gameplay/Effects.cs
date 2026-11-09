using Godot;

namespace Isolith.Gameplay;

/// <summary>
/// One-shot visual effects.
/// </summary>
/// <remarks>
/// Everything here is built in code, like the rest of the game's look — there
/// are no particle scene files to keep in sync. An effect is a short-lived node
/// that frees itself, so a burst cannot accumulate in the tree over a long run.
///
/// Effects are cosmetic only. Nothing in gameplay reads one, and a headless
/// build without a renderer skips them entirely.
/// </remarks>
public static class Effects
{
    /// <summary>Emits a small radial burst of emissive motes.</summary>
    public static void Burst(
        Node parent,
        Vector3 position,
        Color color,
        int amount = 18,
        float speed = 4.5f,
        float lifetime = 0.6f)
    {
        if (parent is null || !GodotObject.IsInstanceValid(parent) || !parent.IsInsideTree())
            return;

        // Headless runs (CI, the smoke test) have no renderer; creating a GPU
        // particle system there is pointless and noisy.
        if (DisplayServer.GetName() == "headless")
            return;

        var mesh = new BoxMesh { Size = Vector3.One * 0.09f };
        mesh.Material = new StandardMaterial3D
        {
            AlbedoColor = color,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 2.2f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };

        var particles = new GpuParticles3D
        {
            Amount = amount,
            Lifetime = lifetime,
            OneShot = true,
            Explosiveness = 1.0f,
            DrawPass1 = mesh,
            ProcessMaterial = new ParticleProcessMaterial
            {
                Direction = Vector3.Up,
                Spread = 180.0f,
                InitialVelocityMin = speed * 0.35f,
                InitialVelocityMax = speed,
                Gravity = new Vector3(0, -9.8f, 0),
                ScaleMin = 0.5f,
                ScaleMax = 1.3f,
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.12f,
            },
        };

        parent.AddChild(particles);
        particles.GlobalPosition = position;

        // Exploding on the first frame rather than waiting for a process tick
        // makes a collect land on the same frame as the sound.
        particles.Emitting = true;

        parent.GetTree()?.CreateTimer(lifetime + 0.25f).Timeout += () =>
        {
            if (GodotObject.IsInstanceValid(particles))
                particles.QueueFree();
        };
    }
}
