# Ecosystem: addons and asset sources

A survey of Godot addons and CC0 asset sources, judged against Isolith's own
constraints. Nothing here is installed — this is the evaluation, so the decision
isn't re-litigated from scratch each time.

## How anything gets adopted

Two rules already in force decide most of this before taste enters into it.

**The language policy** (§2 of [`AGENTS.md`](../AGENTS.md)): all runtime code is
C#; GDScript is editor-only. Most Godot addons are GDScript that runs at
game time. Calling into one from C# means `Variant` marshalling at every call,
no compile-time checking across the seam, and a language boundary through the
middle of the project — exactly what the policy exists to prevent.

So addons sort into three bins:

| Kind | Verdict |
| --- | --- |
| **GDExtension** (native C++, C# bindings) | Fine. No language boundary — it's an engine feature. |
| **C# library / NuGet** | Fine. |
| **GDScript, editor-only** (`addons/`, `@tool`) | Fine. This is exactly what §2 permits GDScript for. |
| **GDScript, runtime** | Rejected by policy. |

**The asset policy** ([`ASSETS.md`](../ASSETS.md)): everything shipped is
script-generated or hand-authored as source text. Third-party assets are not
currently used at all — see [Asset sources](#asset-sources) for what adopting
some would actually cost.

---

## Addons

### Worth using

**Jolt Physics** — *built into the engine, not an addon.*
Since Godot 4.4 the Jolt integration ships as an engine module; the old
[`godot-jolt`](https://github.com/godot-jolt/godot-jolt) GDExtension is in
maintenance mode. Enable it with the `physics/3d/physics_engine` project
setting. Jolt's character handling is generally better behaved on slopes,
moving platforms, and against seams between adjacent collision boxes — all
things a box-built platformer runs into.

Still flagged **experimental** in the docs and not a full drop-in for Godot
Physics, so treat it as an experiment: flip the setting, run the smoke test, and
play the ascent looking for changes to jump feel. Cost is one line, and reverting
is the same line.

**[Debug Draw 3D](https://github.com/DmitriySalnikov/godot_debug_draw_3d)** —
GDExtension, C# supported.
Immediate-mode 3D lines, boxes, and text drawn from code. For tuning a character
controller this beats `GD.Print` outright: draw the velocity vector, the floor
normal, and the coyote/jump-buffer timers in world space and the feel problems
become visible instead of inferred. Being a GDExtension, it sits on the right
side of the language policy.

Worth gating behind `#if DEBUG` so it never reaches an exported build.

**[gdUnit4](https://github.com/MikeSchulze/gdUnit4)** (with `gdUnit4Net` for C#) —
Only if the test suite grows past the smoke test. Isolith's current testing is
one headless scene that drives real gameplay, which is the right size for the
project today. If unit-level tests appear — course parsing, `RunRecord`
round-trips, `RunStats` ordering — gdUnit4Net gives them a proper runner, and
it's a NuGet package rather than a GDScript addon.

### Not for this project

**[Phantom Camera](https://store.godotengine.org/asset/ramokz/phantom-camera/)** —
The best-known Godot camera addon, Cinemachine-style: follow behaviours,
transitions, collision avoidance. Two problems here. It's GDScript at runtime,
so the language policy rules it out; and Isolith's camera is deliberately
special-purpose — fixed isometric pitch, 90° snap rotation, and a `Yaw` that
reports the destination of a turn rather than the animating value, which is what
keeps held input meaningful mid-rotation. A general camera rig would have to be
bent back into that shape.

**Terrain3D** — GDExtension and well regarded, but Isolith has no terrain. Levels
are boxes described in JSON, on purpose.

**Beehave** (behaviour trees), **Dialogic** (dialogue), **input helper addons** —
No AI, no dialogue, and device detection is already ~10 lines in `GameInput`.

**Godot Git Plugin** — Editor-only, so policy-compatible, but irrelevant to
anything the repository does.

---

## Asset sources

All of the following are CC0 (public domain, no attribution required), so they
satisfy the original "free (CC0) or generated yourself" constraint.

| Source | Content | Notes |
| --- | --- | --- |
| [Kenney](https://kenney.nl/assets) | 3D kits, textures, UI, audio | The largest CC0 game-asset library. Consistent style, shared texture atlases, sane poly counts. Includes a platformer kit and prototype textures. |
| [Quaternius](https://quaternius.com/) | Low-poly 3D models | Packs organised by genre, so a whole scene can be built from matching pieces. |
| [KayKit](https://kaylousberg.itch.io/) | Stylised 3D kits, characters | Most packs CC0 — check each pack's page, a few differ. |
| [Poly Haven](https://polyhaven.com/) | HDRIs, PBR textures, scanned models | HDRIs would be the obvious first adoption: a real sky is hard to beat procedurally. |
| [ambientCG](https://ambientcg.com/) | 1500+ PBR materials | Would pair with triplanar mapping on the existing box geometry. |
| [Freesound](https://freesound.org/) (filter to CC0) | Audio | Mixed licences — the CC0 filter is mandatory, not optional. |
| [OpenGameArt](https://opengameart.org/) (filter to CC0) | Mixed | Same caveat, and quality varies a lot more. |
| [awesome-cc0](https://github.com/madjin/awesome-cc0) | Index | A maintained list of the above and more. |

### What adopting any of them would cost

Isolith currently has a property worth naming before trading it away: **the
repository contains no third-party binary content at all.** Every asset is
either a script that produces it or text you can read in a diff. That is why
`ASSETS.md` can be a complete, short, verifiable table.

Bringing in a pack means:

1. Adding it to the `ASSETS.md` table with its source, version, and licence —
   the table is meant to stay exhaustive.
2. Committing binaries, and losing the CI check that regenerates assets and
   fails on any diff. That check only works for things we generate.
3. Auditing per-file, not per-site. Kenney and Quaternius are wholly CC0;
   OpenGameArt, Freesound, and some KayKit packs are per-item, and "the site is
   mostly CC0" is not a licence.
4. Deciding where the seam is. Mixing authored art with the current
   code-generated palette tends to look worse than either alone. A coherent
   result probably means either full adoption of one pack's style, or using
   third-party assets only where procedural genuinely can't compete.

**Recommendation:** if anything is adopted first, make it an HDRI or a PBR
material set — those raise the visual floor a lot for one file and don't fight
the code-generated geometry. Adopting a full model kit is a change of art
direction, not an addition, and should be a deliberate decision rather than a
default.

---

## Sources

- [Best Plugins for Godot (Vagon)](https://vagon.io/blog/best-plugins-for-godot)
- [16 Free Godot 4 Plugins Worth Installing (2026)](https://gamineai.com/blog/16-free-godot-4-plugins-worth-installing-before-your-first-vertical-slice-2026)
- [Phantom Camera — Godot Asset Store](https://store.godotengine.org/asset/ramokz/phantom-camera/)
- [Using Jolt Physics — Godot Engine documentation](https://docs.godotengine.org/en/latest/tutorials/physics/using_jolt_physics.html)
- [godot-jolt (maintenance mode)](https://github.com/godot-jolt/godot-jolt)
- [Godot 4.4 Gets Native Jolt Physics Support — GameFromScratch](https://gamefromscratch.com/godot-4-4-gets-native-jolt-physics-support/)
- [awesome-cc0](https://github.com/madjin/awesome-cc0)
