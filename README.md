# Isolith

An isometric 3D platformer, built with Godot 4.7 and C#.

Jump between floating stone platforms, collect shards, don't fall in the spikes.
It's a game first: it plays offline, keeps its own local history, and needs no
account. Optionally, it can also copy your run stats into your own AT Protocol
repo — see [Stat sync](#stat-sync-optional), which is genuinely optional.

> Independent project; see the [trademark notice](TRADEMARKS.md).

## Features

- **True isometric camera** — orthographic, pitched to `atan(1/√2)`, rotatable
  in 90° steps so geometry never hides the route.
- **Platformer feel that forgives** — coyote time, jump buffering, variable jump
  height, and separate rise/fall gravity.
- **Gamepad first** — the analog stick drives speed directly; keyboard is the
  fallback, and on-screen hints follow whichever you last touched.
- **Levels as data** — courses are JSON, built into geometry at load time. No
  binary scene files to merge, and every level is a readable diff.
- **Course hazards** — moving platforms you ride, platforms that crumble under
  you, bounce pads, spike pits, and checkpoints.
- **No third-party assets.** Every sound is synthesised by a script in this
  repository; every mesh and material is built in code. See [ASSETS.md](ASSETS.md).

## Requirements

| | |
| --- | --- |
| Godot | 4.7 (.NET / Mono build) |
| .NET SDK | 10.0 |
| Python | 3.10+ — only to regenerate audio |

## Getting started

```bash
git clone https://github.com/ewanc26/isolith.git
cd isolith
dotnet build
```

Then open `project.godot` in Godot 4.7 (.NET) and press F5, or run it directly:

```bash
godot --path .
```

To regenerate the sound effects (they are committed, so this is only needed if
you change the generator):

```bash
python3 tools/generate_assets.py
```

## Controls

Gamepad is the primary scheme; keyboard mirrors it.

| Action | Gamepad | Keyboard |
| --- | --- | --- |
| Move | Left stick / D-pad | WASD or arrows |
| Jump | A | Space |
| Turn view 90° | LB / RB, or flick right stick | Q / E |
| Zoom | Triggers | `+` / `-`, mouse wheel |
| Restart course | Y | R |
| Pause | Start | Esc |
| Sync panel | Back | F1 |

Hold jump for height and release early to cut it short. You get a moment of
coyote time after leaving a ledge, and a jump pressed just before landing is
remembered.

Bindings live in [`src/Core/GameInput.cs`](src/Core/GameInput.cs) rather than in
`project.godot`, so they stay readable in a diff.

## Writing a course

Drop a JSON file in `courses/` and point `GameManager.CoursePath` at it.

```json
{
  "id": "example",
  "name": "Example",
  "spawn": [0, 1.5, 0],
  "killPlaneY": -20,
  "blocks": [
    { "pos": [0, -0.5, 0], "size": [8, 1, 8], "kind": "Grass" },
    { "pos": [0, 0.1, -9], "size": [5, 1, 5], "kind": "Solid" }
  ],
  "movers": [
    { "from": [-25, 3.4, -18], "to": [-25, 3.4, -10],
      "size": [3.5, 0.6, 3.5], "period": 6.0, "phase": 0.0 }
  ],
  "shards": [[0, 1.6, -9]],
  "checkpoints": [[0, 1.8, -25]],
  "goal": [-25, 9.65, 11]
}
```

`kind` is one of `Solid`, `Grass`, `Hazard`, `Bounce`, or `Crumble`. Positions
are centres and sizes are full extents, so a block's walkable surface sits at
`pos.y + size.y / 2`.

A fully held jump clears about **2.4 m of height** and **5.9 m of distance**, so
keep gaps inside that envelope. The smoke test will tell you if the spawn,
checkpoints, or goal end up over a void.

## Testing

```bash
godot --headless --path . res://scenes/Smoke.tscn
```

This loads every course, builds it, checks the built scene matches the data, and
verifies the spawn, every checkpoint, and the goal are all standable — a level
whose goal hangs over nothing is valid JSON but not a finishable course. Exits
non-zero on failure, and runs in CI.

## Stat sync (optional)

Isolith can copy completed runs into your own AT Protocol repo as
`uk.ewancroft.isolith.run` records. It uses [wolfram][wolfram], a C11
implementation of the protocol, through a P/Invoke wrapper in `src/Sync/`.

**This is a side feature.** Every run is saved locally first, unconditionally.
Sync is off by default, the game never prompts for it, and every failure is
non-fatal.

To enable it, build `libwolfram` and drop it in `native/` (see
[`native/README.md`](native/README.md)), then press Back / F1 in game and sign in
with your handle and an **app password**. The password is used for a single
`createSession` call and is never written to disk; no session token is persisted
either, so signing in is per-session by design.

The record schema is in [`lexicons/uk/ewancroft/isolith/run.json`](lexicons/uk/ewancroft/isolith/run.json).
Runs carry a SHA-256 of the course JSON, so times stay comparable only within a
single version of a level — edit a course and old times stay attached to the
layout they were set on.

Only your own repo is read and written. There is no cross-user leaderboard;
that would need an AppView aggregating the collection, which is out of scope here.

## Layout

```
courses/        Level data (JSON)
lexicons/       AT Protocol record schemas
native/         Drop libwolfram here (not vendored)
scenes/         Main.tscn, Player.tscn, Smoke.tscn
src/Core/       Input map, run stats, local history, audio, smoke test
src/Gameplay/   Player, camera, course objects, game manager
src/Level/      Course format, builder, palette
src/Sync/       Optional AT Protocol sync (interop + service)
src/UI/         HUD and sync panel
tools/          Asset generator
```

All runtime code is C#. GDScript is reserved for editor-only tooling, and
Python is repository tooling only — the game builds and runs without it, since
generated assets are committed.

`AGENTS.md` has the full architecture notes, the language policy, the invariants,
and the conventions.

## Licence

Code is [MIT](LICENSE). Assets are additionally released under
[CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/); see
[ASSETS.md](ASSETS.md).

[wolfram]: https://github.com/ewanc26/wolfram
