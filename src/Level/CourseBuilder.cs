using System.Collections.Generic;
using Godot;
using Isolith.Gameplay;

namespace Isolith.Level;

/// <summary>
/// Turns a <see cref="Course"/> into live scene nodes. All geometry is built
/// from Godot primitives at load time, so a level is a small JSON file rather
/// than a binary scene.
/// </summary>
public static class CourseBuilder
{
    /// <summary>Physics layer numbers (1-based, as shown in the Godot editor).</summary>
    public static class Layers
    {
        public const uint World = 1;
        public const uint Player = 2;
        public const uint Trigger = 3;
    }

    /// <summary>Metadata key marking a body the player should bounce off.</summary>
    public const string BounceMeta = "isolith_bounce";

    /// <summary>Everything a freshly built course hands back to the game.</summary>
    public sealed record Built(
        Node3D Root,
        Vector3 Spawn,
        List<Shard> Shards,
        List<Checkpoint> Checkpoints,
        Goal Goal);

    /// <summary>Builds <paramref name="course"/> as a child of <paramref name="parent"/>.</summary>
    public static Built Build(Course course, Node parent)
    {
        var root = new Node3D { Name = $"Course_{course.Id}" };
        parent.AddChild(root);

        foreach (BlockDef block in course.Blocks)
            AddBlock(root, block);

        foreach (MoverDef mover in course.Movers)
            AddMover(root, mover);

        var shards = new List<Shard>();
        foreach (float[] position in course.Shards)
            shards.Add(AddShard(root, Course.ToVector(position)));

        var checkpoints = new List<Checkpoint>();
        foreach (float[] position in course.Checkpoints)
            checkpoints.Add(AddCheckpoint(root, Course.ToVector(position)));

        Goal goal = AddGoal(root, Course.ToVector(course.Goal));

        return new Built(root, Course.ToVector(course.Spawn), shards, checkpoints, goal);
    }

    // -----------------------------------------------------------------------
    // Blocks
    // -----------------------------------------------------------------------

    private static void AddBlock(Node3D root, BlockDef block)
    {
        Vector3 position = Course.ToVector(block.Position);
        Vector3 size = Course.ToVector(block.Size);

        // Hazards are pass-through volumes rather than collision geometry: the
        // player should fall into spikes, not stand on them.
        if (block.Kind == BlockKind.Hazard)
        {
            AddHazard(root, position, size);
            return;
        }

        if (block.Kind == BlockKind.Crumble)
        {
            var crumble = new CrumblePlatform();
            crumble.Configure(size);
            crumble.Position = position;
            root.AddChild(crumble);
            return;
        }

        var body = new StaticBody3D
        {
            Name = $"Block_{block.Kind}",
            Position = position,
            CollisionLayer = Mask(Layers.World),
            CollisionMask = 0,
        };

        if (block.Kind == BlockKind.Bounce)
            body.SetMeta(BounceMeta, true);

        body.AddChild(BoxShape(size));
        body.AddChild(BoxMesh(size, Palette.ForBlock(block.Kind)));
        root.AddChild(body);
    }

    private static void AddHazard(Node3D root, Vector3 position, Vector3 size)
    {
        var area = new Hazard
        {
            Name = "Hazard",
            Position = position,
            CollisionLayer = Mask(Layers.Trigger),
            CollisionMask = Mask(Layers.Player),
            Monitoring = true,
        };

        area.AddChild(BoxShape(size));
        area.AddChild(BoxMesh(size, Palette.Spike));
        root.AddChild(area);
    }

    private static void AddMover(Node3D root, MoverDef mover)
    {
        var platform = new MovingPlatform();
        platform.Configure(
            Course.ToVector(mover.From),
            Course.ToVector(mover.To),
            Course.ToVector(mover.Size),
            mover.Period,
            mover.Phase);
        root.AddChild(platform);
    }

    // -----------------------------------------------------------------------
    // Pickups and markers
    // -----------------------------------------------------------------------

    private static Shard AddShard(Node3D root, Vector3 position)
    {
        var shard = new Shard
        {
            Name = "Shard",
            Position = position,
            CollisionLayer = Mask(Layers.Trigger),
            CollisionMask = Mask(Layers.Player),
        };

        var shape = new CollisionShape3D { Shape = new SphereShape3D { Radius = 0.7f } };
        shard.AddChild(shape);

        // An octahedron reads clearly at isometric angles and needs no art.
        var mesh = new MeshInstance3D
        {
            Mesh = new PrismMesh { Size = new Vector3(0.55f, 0.8f, 0.55f) },
            MaterialOverride = Palette.Collectible,
        };
        shard.AddChild(mesh);
        shard.Visual = mesh;

        shard.AddChild(new OmniLight3D
        {
            LightColor = Palette.Shard,
            LightEnergy = 0.8f,
            OmniRange = 3.5f,
        });

        root.AddChild(shard);
        return shard;
    }

    private static Checkpoint AddCheckpoint(Node3D root, Vector3 position)
    {
        var checkpoint = new Checkpoint
        {
            Name = "Checkpoint",
            Position = position,
            CollisionLayer = Mask(Layers.Trigger),
            CollisionMask = Mask(Layers.Player),
        };

        checkpoint.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(2.0f, 3.0f, 2.0f) },
        });

        var post = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0.14f,
                BottomRadius = 0.2f,
                Height = 2.2f,
            },
            MaterialOverride = Palette.CheckpointIdle,
            Position = new Vector3(0, 1.1f, 0),
        };
        checkpoint.AddChild(post);
        checkpoint.Visual = post;

        root.AddChild(checkpoint);
        return checkpoint;
    }

    private static Goal AddGoal(Node3D root, Vector3 position)
    {
        var goal = new Goal
        {
            Name = "Goal",
            Position = position,
            CollisionLayer = Mask(Layers.Trigger),
            CollisionMask = Mask(Layers.Player),
        };

        goal.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = 1.6f, Height = 3.0f },
        });

        var pad = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 1.6f,
                BottomRadius = 1.6f,
                Height = 0.25f,
            },
            MaterialOverride = Palette.Finish,
        };
        goal.AddChild(pad);
        goal.Visual = pad;

        goal.AddChild(new OmniLight3D
        {
            LightColor = Palette.Goal,
            LightEnergy = 1.6f,
            OmniRange = 8.0f,
            Position = new Vector3(0, 2.0f, 0),
        });

        root.AddChild(goal);
        return goal;
    }

    // -----------------------------------------------------------------------
    // Primitives
    // -----------------------------------------------------------------------

    internal static CollisionShape3D BoxShape(Vector3 size) =>
        new() { Shape = new BoxShape3D { Size = size } };

    internal static MeshInstance3D BoxMesh(Vector3 size, Material material) =>
        new() { Mesh = new BoxMesh { Size = size }, MaterialOverride = material };

    /// <summary>Converts a 1-based layer number into a collision bitmask.</summary>
    internal static uint Mask(uint layerNumber) => 1u << (int)(layerNumber - 1);
}
