#!/usr/bin/env python3
"""Build Isolith's player character and export it as glTF.

Run through Blender, which supplies the ``bpy`` module:

    blender -b --python tools/generate_player_model.py

Output: ``assets/models/player.glb``.

Why generated rather than downloaded: the CC0 character packs worth having
(Kenney, Quaternius, KayKit) are all browser- or itch-gated, with no stable URL
a script can pin and checksum. Everything else in this repository is either
reproducible from a script or pinned by hash, and a character dragged manually
out of a browser is neither.

The character is **segmented, not skinned**. Each limb is its own object with its
origin at the joint it pivots around, so Godot animates it by rotating nodes —
no armature, no baked animation clips, and a run cycle that responds to actual
speed rather than playing back at a fixed rate. This is the same approach
Kenney's blocky characters use, and it survives a round trip through glTF
without any rig-import subtleties.

Object names are the contract with ``src/Gameplay/PlayerVisual.cs``; renaming one
here breaks the animation there.
"""

from __future__ import annotations

import pathlib
import sys

try:
    import bpy
    from mathutils import Matrix, Vector
except ImportError:  # pragma: no cover - only reachable outside Blender
    sys.exit("error: run this through Blender:\n"
             "       blender -b --python tools/generate_player_model.py")


# Matches src/Level/Palette.cs so the character sits in the same world as the
# level geometry.
PALETTE = {
    "body": (0.949, 0.941, 0.921, 1.0),
    "trim": (1.000, 0.561, 0.369, 1.0),
    "dark": (0.251, 0.290, 0.388, 1.0),
    "visor": (0.310, 0.765, 0.851, 1.0),
}

OUTPUT = pathlib.Path(__file__).resolve().parent.parent / "assets" / "models" / "player.glb"


# ---------------------------------------------------------------------------
# Scene helpers
# ---------------------------------------------------------------------------


def clear_scene() -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name: str, colour) -> bpy.types.Material:
    existing = bpy.data.materials.get(name)
    if existing:
        return existing

    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = colour
    bsdf.inputs["Roughness"].default_value = 0.55
    bsdf.inputs["Metallic"].default_value = 0.0
    return mat


# Blender is Z-up; glTF and Godot are Y-up. Rather than author in Blender space
# and hope the exporter's conversion lands where intended, everything below is
# written in **game space** and converted here. Authoring in the target space is
# what makes the numbers checkable against the .tscn and the controller.
def to_blender(point):
    """Game-space (x right, y up, z forward) -> Blender (x right, y back, z up)."""
    return (point[0], -point[2], point[1])


def size_to_blender(size):
    """Game-space (width, height, depth) -> Blender (width, depth, height)."""
    return (size[0], size[2], size[1])


def box(name: str, size, location, pivot=None, colour="body", bevel=0.02):
    """A bevelled box whose origin sits at ``pivot`` rather than its centre.

    Arguments are in game space. The origin is what Godot rotates around, so an
    arm's origin belongs at the shoulder and a leg's at the hip; getting this
    wrong makes limbs orbit their own middles, which reads as a broken puppet.
    """
    size = size_to_blender(size)
    location = to_blender(location)
    if pivot is not None:
        pivot = to_blender(pivot)

    bpy.ops.mesh.primitive_cube_add(size=1.0, location=location)
    obj = bpy.context.active_object
    obj.name = name
    obj.scale = Vector(size) * 0.5

    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    if bevel > 0:
        modifier = obj.modifiers.new("Bevel", "BEVEL")
        modifier.width = bevel
        modifier.segments = 2
        modifier.limit_method = "ANGLE"
        bpy.ops.object.modifier_apply(modifier="Bevel")

    if pivot is not None:
        # Move the origin without moving the geometry.
        offset = obj.location - Vector(pivot)
        obj.data.transform(Matrix.Translation(offset))
        obj.location = Vector(pivot)

    ORIGINS[obj.name] = tuple(obj.location)
    obj.data.materials.append(material(colour, PALETTE[colour]))
    bpy.ops.object.shade_smooth()

    # Sharp silhouette, soft shading only where the bevel wants it.
    obj.data.polygons.foreach_set("use_smooth", [False] * len(obj.data.polygons))
    return obj


# World-space origin of each object, in Blender coordinates, recorded as it is
# built so parenting can be expressed as a plain local offset.
ORIGINS: dict[str, tuple] = {}


def parent_to(child, parent) -> None:
    """Parent with an identity parent-inverse and a pure local translation.

    Blender's ``parent_set(keep_transform=True)`` stashes the correction in
    ``matrix_parent_inverse``, which glTF cannot express — the exporter bakes it
    into the node's local transform, and it comes out of the importer as a
    non-identity *rotation* on the limb. Any code that then sets a limb's
    rotation (which is the whole animation approach here) destroys that
    correction and the character falls apart.

    Nothing is rotated at build time, so the correct local transform is simply
    the difference between the two origins. Every exported limb therefore has an
    identity rotation, which is exactly what PlayerVisual expects to animate
    from.
    """
    child_origin = Vector(ORIGINS[child.name])
    parent_origin = Vector(ORIGINS.get(parent.name, (0.0, 0.0, 0.0)))

    child.parent = parent
    child.matrix_parent_inverse = Matrix.Identity(4)
    child.location = child_origin - parent_origin
    child.rotation_euler = (0.0, 0.0, 0.0)


# ---------------------------------------------------------------------------
# The character
# ---------------------------------------------------------------------------


def build() -> None:
    clear_scene()

    # Proportions are deliberately chunky. At the isometric camera's distance a
    # realistically proportioned figure reads as a smudge; a large head and
    # heavy feet keep the silhouette legible.
    root = bpy.data.objects.new("PlayerModel", None)
    bpy.context.collection.objects.link(root)
    root.empty_display_size = 0.2
    ORIGINS[root.name] = (0.0, 0.0, 0.0)

    torso = box("Torso", (0.52, 0.62, 0.36), (0, 0.95, 0), colour="body")
    hips = box("Hips", (0.46, 0.22, 0.34), (0, 0.63, 0), colour="dark")

    head = box("Head", (0.50, 0.44, 0.46), (0, 1.44, 0), pivot=(0, 1.24, 0), colour="body")

    # A visor rather than a face: readable from any of the four camera yaws, and
    # it makes the facing direction obvious at a glance.
    visor = box("Visor", (0.40, 0.14, 0.06), (0, 1.46, 0.22), colour="visor", bevel=0.01)

    # Arms pivot at the shoulder.
    arm_l = box("ArmL", (0.16, 0.52, 0.16), (0.34, 0.92, 0), pivot=(0.34, 1.16, 0), colour="trim")
    arm_r = box("ArmR", (0.16, 0.52, 0.16), (-0.34, 0.92, 0), pivot=(-0.34, 1.16, 0), colour="trim")

    # Legs pivot at the hip.
    leg_l = box("LegL", (0.19, 0.56, 0.19), (0.14, 0.30, 0), pivot=(0.14, 0.56, 0), colour="dark")
    leg_r = box("LegR", (0.19, 0.56, 0.19), (-0.14, 0.30, 0), pivot=(-0.14, 0.56, 0), colour="dark")

    foot_l = box("FootL", (0.22, 0.12, 0.32), (0.14, 0.06, 0.05), colour="trim", bevel=0.015)
    foot_r = box("FootR", (0.22, 0.12, 0.32), (-0.14, 0.06, 0.05), colour="trim", bevel=0.015)

    for child in (torso, hips, head, arm_l, arm_r, leg_l, leg_r):
        parent_to(child, root)

    parent_to(visor, head)
    parent_to(foot_l, leg_l)
    parent_to(foot_r, leg_r)

    export(root)


def export(root) -> None:
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)

    bpy.ops.object.select_all(action="SELECT")
    bpy.context.view_layer.objects.active = root

    bpy.ops.export_scene.gltf(
        filepath=str(OUTPUT),
        export_format="GLB",
        export_apply=True,
        export_yup=True,
        export_cameras=False,
        export_lights=False,
        export_animations=False,
    )

    size = OUTPUT.stat().st_size
    print(f"wrote {OUTPUT} ({size / 1024:.0f} KB)")


if __name__ == "__main__":
    build()
