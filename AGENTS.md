# AGENTS.md

Working notes for anyone — human or agent — making changes to Isolith. It
records the architecture, the invariants that are easy to break, and the
reasoning behind decisions that look arbitrary from the outside.

Read [`README.md`](README.md) first for what the game is. This file is about how
it is built.

---

## 1. What this project is

**Isolith is an isometric 3D platformer.** Godot 4.7, C#, .NET 10.

It is a game first. It runs offline, needs no account, and stores its full run
history locally. An optional module can additionally copy run stats into the
player's own AT Protocol repo.

### Scope

| In scope | Out of scope |
| --- | --- |
| Single-player platforming | Multiplayer, netcode |
| Data-driven courses (JSON) | An in-engine level editor |
| Local run history | A server, an account system |
| Optional per-user repo sync | Cross-user leaderboards (needs an AppView) |
| Code-generated assets | Imported art, audio, or fonts |

### The one rule that shapes everything

**Sync is never load-bearing.** If `libwolfram` is missing, the network is down,
the player never signs in, or a record write fails, the game must play exactly
the same. Every code path in `src/Sync/` is allowed to fail; no code path in
`src/Gameplay/` may depend on one succeeding.

If you find yourself writing `if (syncFailed) { …gameplay change… }`, stop.

---

## 2. Languages

Three languages appear in this repository. The split between them is strict.

| Language | Used for | Never used for |
| --- | --- | --- |
| **C#** | Every line of runtime game code | — |
| **GDScript** | Editor-only tooling (`@tool` scripts, `EditorPlugin`s under `addons/`) | Anything the running game loads |
| **Python** | Repository tooling under `tools/`, and CI | Anything at game runtime, or any build step the game needs |

### C# is the game

**All runtime code is C#, without exception.** Gameplay, level building, UI,
interop, and the smoke test all live in one assembly under `src/`.

This is not a stylistic preference:

- The sync module talks to a native C library. `LibraryImport` P/Invoke,
  `SafeHandle` ownership, and `CLong`/`nuint` marshalling have no GDScript
  equivalent — that code *must* be C#, and splitting the rest away from it
  would put a language boundary through the middle of the project.
- One assembly means one type system and compile-time checking across every
  module. A `RunStats` passed from gameplay to sync is checked by the compiler,
  not discovered at runtime.
- The smoke test drives real gameplay types directly. Across a script boundary
  it could only poke at them through `Variant`.

### GDScript is for the editor, and nothing else

If GDScript is added, it lives under `addons/` and runs **only inside the Godot
editor** — import plugins, inspector tooling, a course previewer. It is never
attached to a node the game instantiates and is never called from C# at runtime.

The reason for allowing it there at all: an editor plugin written in GDScript
reloads the moment you save it, whereas a C# `[Tool]` script needs an assembly
rebuild and usually an editor restart. That difference matters for tooling you
iterate on and matters not at all for code that ships.

**There is currently no GDScript in this repository.** That is the default
state, not an accident. Adding some requires a reason that fits the box above.

This rules out most Godot addons, which are GDScript running at game time.
GDExtension addons (native, with C# bindings) and NuGet packages are fine.
[`docs/ecosystem.md`](docs/ecosystem.md) works through the specific ones worth
considering.

### Python is tooling, never runtime

`tools/generate_assets.py` is a development script. It uses the standard library
only, and its output is **committed**, so:

- the game builds and runs on a machine with no Python installed;
- Python is never a build dependency, only a regeneration convenience;
- CI can verify the committed output still matches the generator.

### Explicitly not allowed

- Game logic split across C# and GDScript. Two type systems, `Variant`
  marshalling at every call, and no compile-time checking across the seam.
- Calling GDScript from C# (or the reverse) in anything the game loads.
- Python as a runtime or build-time dependency of the game.
- Shipping a generated artefact that is not committed alongside its generator.

---

## 3. Orientation

```bash
dotnet build                                              # compile
godot --path .                                            # play
godot --headless --path . res://scenes/Smoke.tscn         # test
godot --path . res://scenes/Screenshot.tscn               # stills (needs a display)
python3 tools/generate_assets.py                          # regenerate audio
blender -b --python tools/generate_player_model.py        # regenerate the player model
```

On macOS the Godot binary is inside the app bundle:

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --path .
```

The project must be built with `dotnet build` **before** Godot can run it —
Godot loads the compiled assembly from `.godot/mono/temp/bin/`, it does not
compile C# itself.

---

## 4. Layout

```
.github/workflows/ci.yml   Build + smoke test + asset reproducibility
addons/                    Editor-only plugins (GDScript lives here, if ever)
assets/audio/              Generated WAVs (committed, reproducible)
assets/models/             Generated glTF (committed, reproducible)
docs/                      Ecosystem survey (addons, CC0 asset sources)
docs/shots/                Screenshots, generated by scenes/Screenshot.tscn
courses/                   Level data
lexicons/                  AT Protocol record schemas
native/                    libwolfram goes here (gitignored, not vendored)
scenes/                    Menu.tscn (main scene), Main.tscn, Player.tscn, Smoke.tscn
src/Core/                  Input, settings, stats, history, audio, smoke test
src/Gameplay/              Player, camera, course objects, game manager
src/Level/                 Course format, builder, palette
src/Level/Generation/      Endless mode: director, generator, envelope
src/Sync/                  Optional AT Protocol sync
src/UI/                    Title screen, settings, HUD and sync panel
tools/                     Asset generators
```

### Scene entry points

`Menu.tscn` is the **main scene**, not `Main.tscn`. A session is created by the
title screen and freed when it ends, so nothing carries between runs — a paused
tree, a half-torn-down endless course, a locked player. `Main.tscn` still runs
standalone (F6 in the editor, and the smoke test instantiates it directly), which
is why `GameManager._Ready` repeats the startup that the menu would otherwise
have done.

### Dependency direction

```
        UI ──────────────┐
         │               │
         ▼               ▼
     Gameplay ────────► Core ◄──── Sync
         │
         ▼
       Level
```

- `Core` depends on nothing but Godot. It is the shared vocabulary
  (`RunStats`, `GameInput`, `RunHistory`, `Sfx`).
- `Level` is pure content: parsing course JSON and turning it into nodes.
- `Gameplay` owns the session. It knows about `Level` and `Core`.
- `Sync` knows about `Core` only. **It must never reference `Gameplay`.**
- `UI` is the only layer allowed to wire `Gameplay` and `Sync` together, and it
  does so through events in one direction: gameplay raises, sync reacts.

`SmokeTest` and `GenerationTests` live in `Core` but reach into `Gameplay` and
`Level` — a deliberate exception for test harnesses, not a licence to add more.

---

## 5. Module notes

### `src/Core/GameInput.cs`

Action names and default bindings.

Bindings are registered **in code**, not in `project.godot`. Godot serialises an
input map as a dense `Object(InputEventKey, …)` blob that is unreviewable in a
diff and shifts between engine versions. `Configure()` skips any action that
already exists, so an `[input]` section in `project.godot` or a player's own
remapping still wins.

**Gamepad is primary.** Each action lists its controller binding first and its
keyboard binding second. Movement uses `Input.GetVector`, which preserves stick
magnitude — analog input controls speed, keys are all-or-nothing.

`Observe()` tracks which device was last used so the HUD can name the right
button. It ignores joystick motion below 0.35 so stick drift doesn't keep
claiming the pad is in use.

Keys bind by **physical** position (`PhysicalKeycode`), so WASD stays under the
same fingers on AZERTY.

### `src/Core/RunStats.cs` / `RunHistory.cs`

`RunStats` is one attempt. `RunHistory` persists a capped list to
`user://runs.json`.

This is the authoritative store. `GameManager.OnGoalReached` writes it
*unconditionally, before* raising `RunCompleted`. Sync is a listener on that
event, never a gatekeeper of it.

A corrupt history file logs a warning and reads as empty. It must never throw
into gameplay.

### `src/Core/Settings.cs`

Player preferences, in a `ConfigFile` at `user://settings.cfg`. An INI rather
than JSON because a settings file is something a player may reasonably want to
open and edit.

**A setting is not done until something reads it.** The failure mode here is a
preference that persists perfectly and configures nothing, which looks identical
from the outside. Every setter applies its change immediately; anything that has
to be pushed into the engine once at startup goes through `Apply()`.

Where each one lands:

| Setting | Read by |
| --- | --- |
| `MasterVolume`, `Fullscreen` | `Apply()`, into the Master bus and `DisplayServer` |
| `EffectsVolume` | `Sfx.Play` |
| `CameraZoom` | `IsometricCamera`, which also writes back when zoomed in game |
| `ShowDirectorNotes` | `Hud.OnSectionCompleted` |
| `AutoSync`, `SyncHandle`, `SyncService` | `SyncPanel` |

`Apply()` runs after *every* setter, and camera zoom is a setter the player can
hit several times a second, so it must stay cheap and idempotent — hence the
window-mode guard.

The generic accessors need `[MustBeVariant]`. That attribute is what lets a type
parameter cross the `Variant` boundary at all; without it the Godot analyser
rejects the call as GD0302.

### `src/Core/Sfx.cs`

Round-robin pool of eight `AudioStreamPlayer` voices. Clips load from
`res://assets/audio/`; **missing clips are silently skipped**, so a fresh clone
that hasn't run the generator still plays.

`ProcessMode = Always` so menu sounds work while the tree is paused.

Effects volume is applied per clip; master volume is not, because that one rides
on the Master bus and so also covers anything that is not an effect. Zero effects
volume skips playback entirely rather than queueing a silent voice — a -80 dB
voice still occupies the pool and still cuts off the sound behind it.

### `src/Core/ScreenshotTool.cs`

Stills for the README, and a way to look at a visual change without playing
through to it. Needs a real renderer, so unlike the smoke test it cannot run
headless:

```bash
godot --path . res://scenes/Screenshot.tscn
```

### `src/Level/Course.cs`

The course data model plus `Load`/`Parse`.

Loading goes through Godot's `FileAccess`, not `System.IO`, because in an
exported build `res://` lives inside the `.pck` and is not a real file.

`Course.Hash` is the SHA-256 of the **exact source JSON**, kept in
`SourceJson`. It is what makes times comparable: edit a course and every prior
run stays attached to the layout it was set on. Do not compute the hash from
re-serialised data — whitespace changes would silently invalidate history.

### `src/Level/CourseBuilder.cs`

Turns a `Course` into nodes. The one place that decides what a block *is*.

Physics layers (1-based, matching the editor and `project.godot`):

| Layer | Name | Contents |
| --- | --- | --- |
| 1 | World | Solid geometry, moving and crumbling platforms |
| 2 | Player | The character body |
| 3 | Trigger | Shards, checkpoints, goal, hazards, crumble triggers |

`Mask(n)` converts a layer *number* into a bitmask. Passing a mask where a
number is expected is the classic bug here.

**Hazards are `Area3D`, not `StaticBody3D`.** The player should fall *into*
spikes, not stand on them.

**Bounce pads are flagged with metadata**, not a type. `CourseBuilder` sets
`BounceMeta` on the body and `PlayerController` reads it off whatever it lands
on. This keeps the controller ignorant of level semantics — adding a new
surface behaviour shouldn't mean editing the character controller.

### `src/Level/Palette.cs`

Every colour and material in the game, built in code and cached. There are no
material resources on disk. If you are about to hardcode a `Color` anywhere
else, put it here instead.

### `src/Gameplay/PlayerController.cs`

The most tuning-sensitive file. See §6 for the numbers.

Input is interpreted in the **camera's** frame, not the world's, via
`Camera.Yaw`. Without that, a 45°-rotated view makes every direction diagonal.

`FloorSnapLength = 0.4` keeps the character stuck to downward slopes and to
moving platforms instead of stepping off into a one-frame fall.

The small `-2.0` downward velocity applied while grounded is deliberate: it
keeps `IsOnFloor()` stable frame to frame. Removing it causes intermittent
false airborne states and breaks coyote timing.

### `src/Gameplay/PlayerVisual.cs`

Poses the character's limbs each frame from what the controller is actually
doing. The model is **segmented, not skinned** (see `ASSETS.md`), so there is no
armature and there are no animation clips.

Stride phase advances with *distance covered*, not with time. A baked run cycle
plays at a fixed rate and desynchronises the moment the player is accelerating,
airborne, or riding a moving platform; driving the phase from measured horizontal
speed keeps the feet matched to the ground.

Poses are applied as a **delta from the imported rest transform**, not as an
absolute rotation. Overwriting a limb outright would discard whatever the glTF
importer set up and move the limb off its joint.

Limb node names are the contract with `tools/generate_player_model.py`. If they
do not match, it warns once, clearly, rather than standing still all session.

### `src/Gameplay/IsometricCamera.cs`

Orthographic, pitched to `-atan(1/√2) ≈ -35.264°`. At that angle a unit cube
projects to a regular hexagon with three equally foreshortened faces — that is
what makes it *isometric* rather than merely angled. Changing the pitch breaks
the projection; change `ViewSize` if you want a different framing.

`Yaw` returns the **destination** yaw, not the animating one. Snapping the
control frame the moment a rotation starts keeps a held direction meaningful
throughout the turn; interpolating it would curve the player's path mid-rotation.

Follow smoothing uses `1 - exp(-sharpness * dt)`, which is frame-rate
independent. A plain `Lerp(a, b, 0.1f)` is not, and will feel different at 144 Hz.

`ViewSize` is read from `Settings.CameraZoom` at `_Ready` and written back when
the player uses the zoom controls, so the in-game keys and the settings slider
are two views of one value rather than two settings that can disagree. The
exported default is therefore only what a scene starts at before any preference
exists.

### `src/Gameplay/GameManager.cs`

Owns the session: load, build, time, die, restart, complete.

**Required children** of the node this script is attached to:

| Name | Type |
| --- | --- |
| `Player` | `PlayerController` |
| `IsoCamera` | `IsometricCamera` |
| `CourseRoot` | `Node3D` |

`RequireChild<T>` throws a descriptive error if one is missing. `Hud` and
`SyncPanel` also expect to be children, and `SyncPanel` looks up its sibling by
the name `Hud`.

`Restart()` rebuilds the course from scratch rather than resetting objects
individually. Cheap at this scale, and it makes "did I reset everything?" a
non-question.

`SetUiFocus(bool)` suppresses gameplay input while a panel holds the keyboard,
so typing a handle into a text field doesn't also make the character jump. It
also gates the Hud's pause and restart handling, which is what lets Escape back
out of the settings panel instead of unpausing underneath it.

**Pause and restart input is not here.** It lives in the Hud, for the reason
given in §5 under `src/UI/`.

### `src/Sync/`

See §9. Structure:

| File | Tier |
| --- | --- |
| `Interop/WolframLibrary.cs` | Locates and loads the native library |
| `Interop/WolframNative.cs` | Raw `LibraryImport` declarations, 1:1 with the C ABI |
| `Interop/WolframAgentHandle.cs` | `SafeHandle` owning `wf_agent *` |
| `WolframAgent.cs` | Managed, blocking API |
| `WolframStatus.cs`, `WolframException.cs` | Status mirror and errors |
| `RunRecord.cs` | `RunStats` ⇄ lexicon JSON |
| `SyncService.cs` | Async Godot node, the only thing UI touches |

### `src/UI/`

Built entirely in code. The UI is text on flat panels; describing it in C# keeps
it reviewable in a diff and avoids shipping a binary theme resource. If the UI
grows past a few panels, revisit this — it is a pragmatic choice, not dogma.

| File | Responsibility |
| --- | --- |
| `MenuKit.cs` | Shared widgets, styling, and focus wiring |
| `MainMenu.cs` | The title screen, and the only place that starts or ends a session |
| `SettingsPanel.cs` | One settings screen, shown by both the title screen and the pause menu |
| `Hud.cs` | In-game overlay, the pause menu, and pause/restart input |
| `SyncPanel.cs` | The optional sync panel |

**Gamepad is primary, so a menu that needs a mouse is a broken menu.** Every
control gets an explicit focus neighbour: Godot's automatic neighbour search
works from on-screen geometry and gets confused by nested containers.
`SmokeTest.CheckMenu` fails the build if a control is left stranded.

`MenuKit.FocusChain` deliberately does **not** grab focus — menus are built once
and shown many times, often while hidden and often two at a time, so a chain that
focused as a side effect of construction would fight whichever menu is actually
on screen. `MenuKit.Focus` is the separate, deferred step, deferred because Godot
refuses to focus a control that the layout pass has not yet made visible.

#### Pause lives in the Hud, not in `GameManager`

This looks misplaced and is not. `GameManager` is pausable, so its `_Process`
stops the instant the tree pauses — it can pause the game but can never see the
input that would unpause it again. The Hud runs with `ProcessMode.Always` and
owns both halves of the toggle, plus restart.

It is also **event-driven rather than polled**. `Input.IsActionJustPressed` stays
true for the rest of the frame, so a polled unpause would let one Escape resume
the game and then immediately re-pause it.

`SetUiFocus(true)` suppresses that handling while a panel is up, which is what
makes Escape back out of the settings panel instead of unpausing underneath it.

---

## 6. The physics envelope

These derive from `PlayerController`'s exported defaults. **If you change the
tuning, recompute these and update `README.md`, because course design depends
on them.**

| Constant | Value |
| --- | --- |
| `MoveSpeed` | 7.0 m/s |
| `JumpHeight` | 2.45 m |
| `RiseGravity` | 22.0 m/s² |
| `FallGravity` | 36.0 m/s² |
| `BounceHeight` | 5.5 m |
| `CoyoteTime` | 0.12 s |
| `JumpBufferTime` | 0.14 s |

Jump velocity is `√(2 · RiseGravity · JumpHeight)`:

```
v      = √(2 × 22 × 2.45)   = 10.38 m/s
t_up   = 10.38 / 22          = 0.472 s
t_down = √(2 × 2.45 / 36)    = 0.369 s
airtime                      = 0.841 s
range  = 7.0 × 0.841         ≈ 5.9 m
```

Bounce pad:

```
v      = √(2 × 22 × 5.5)     = 15.56 m/s
apex                          = 5.5 m
airtime                       ≈ 1.26 s
range                         ≈ 8.8 m
```

**Design rule:** keep gaps under ~5 m horizontal and ~2.2 m vertical for a
normal jump. Rise gravity is deliberately lower than fall gravity — a floaty
rise and a fast drop is what makes a platformer feel responsive rather than
sluggish.

---

## 7. Course format

A course is a JSON object in `courses/`. All positions are **centres**; all
sizes are **full extents**. A block's walkable surface is at
`pos.y + size.y / 2`.

| Field | Type | Notes |
| --- | --- | --- |
| `id` | string | Stable identifier; goes into run records |
| `name` | string | Display name |
| `author` | string | Optional |
| `spawn` | `[x, y, z]` | Player start |
| `killPlaneY` | number | Falling below this is a death |
| `blocks` | array | Static geometry — see below |
| `movers` | array | Moving platforms |
| `shards` | `[[x, y, z], …]` | Collectibles |
| `checkpoints` | `[[x, y, z], …]` | Respawn markers |
| `goal` | `[x, y, z]` | Finish pad |

**Block:** `{ "pos": [x,y,z], "size": [w,h,d], "kind": "…" }`

| `kind` | Behaviour |
| --- | --- |
| `Solid` | Plain collision |
| `Grass` | Cosmetic variant of `Solid` |
| `Hazard` | `Area3D`; entering kills |
| `Bounce` | Launches the player on landing |
| `Crumble` | Shakes, drops, and restores after ~3.9 s |

**Mover:** `{ "from": […], "to": […], "size": […], "period": s, "phase": 0–1 }`
— a full there-and-back cycle takes `period`, eased at both ends. `phase`
staggers several movers.

Adding a course means dropping the file in `courses/`. The smoke test discovers
`courses/*.json` automatically and will check it.

---

## 8. Procedural generation

Endless mode. Sections are generated ahead of the player, and each one is shaped
by how the previous one actually went.

### The pieces

| Type | Responsibility |
| --- | --- |
| `JumpEnvelope` | What the player can physically reach, derived from `PlayerController`'s exports |
| `SectionPerformance` | What the player did in one section |
| `SectionSpec` | The recipe for the next section |
| `AdaptiveDirector` | Reads a performance, produces the next spec |
| `SectionGenerator` | Turns a spec into geometry. Pure and deterministic |
| `EndlessCourse` | Streams sections, measures play, frees what is behind |

The split matters: the director is pure logic over plain data, so its behaviour
is tested without building a single node.

### The invariant

**Every generated jump is within reach.** The clamp is against `JumpEnvelope`,
which is computed from the controller's real tuning — not a literal someone
typed. Retune the character and generation retunes with it; there is no second
set of numbers to forget.

`GenerationTests.TraversabilityHolds` asserts this over 3200 jumps spanning the
full difficulty range, using *adversarial* specs whose gap and rise parameters
are deliberately set far beyond the envelope. If the generator's clamping
regresses, that test fails rather than a player finding an uncrossable gap three
minutes into a run they cannot reproduce.

It also checks the opposite failure: generation must actually *reach* the budget
at high difficulty. A generator that clamps everything to something trivial is
safe and pointless.

### What the director reacts to

| Signal from the previous section | Response |
| --- | --- |
| Deaths | Difficulty falls, sharply |
| Died on a moving platform | Mover trust cut to 40%; crumble and bounce untouched |
| Died on a crumbling platform | Crumble trust cut to 45% |
| Walked past bounce pads without using them | Bounce trust falls — they have not understood the mechanic |
| Cleared it with no deaths and good pace | Difficulty rises, slowly |
| Landing repeatedly near platform edges | Difficulty **holds**, even with no deaths |
| Standing still before jumps (hesitation) | Gap *spread* narrows — consistent distances are learnable, varied ones are guesswork |
| Collected every shard comfortably | Shards move onto riskier detours |
| Missed most shards | Shards move back onto the main path |

### Two decisions worth not undoing

**Falls fast, rises slow.** Difficulty may drop 0.20 in a section but rise only
0.06. A player who just died wants relief now; a player who cleared one section
has not yet proven they want it harder. Symmetric rates oscillate — punishing,
then trivial, then punishing again — and the oscillation feels worse than either
extreme.

**Trust is per-mechanic, not one dial.** "Keeps falling off moving platforms" and
"finds the game too easy" are different problems, and a single difficulty number
answers both the same way, which is wrong for at least one of them. Mover,
crumble, and bounce trust move independently, so the game can stay hard in
general while going easy on the one thing this player keeps dying to. Trust also
recovers slowly on its own, so one bad section does not remove a mechanic from
the run permanently.

### Section boundaries

A section is finished when the player triggers the **next** section's
checkpoint. The reaction is therefore to a section actually completed, not to a
guess about where the player is — and the checkpoint already exists for respawn.

### Reproducibility

Generated content goes through the same JSON parse as authored courses, so it is
validated, hashable, and dumpable. `GameManager.Seed` reproduces a run exactly:
set it to a non-zero value and the same sections appear in the same order. That
is what makes a generated level reportable as a bug.

---

## 9. Interop rules (`src/Sync/Interop/`)

This is the only unsafe part of the codebase. Get it wrong and you get memory
corruption, not an exception.

### Ownership

Strings the C SDK returns fall into two classes and **must not** be treated
alike:

| Class | Examples | Handling |
| --- | --- | --- |
| **Borrowed** | `wf_agent_get_did`, `wf_agent_get_handle` | Owned by the agent. Declare the return as `IntPtr` and copy with `Marshal.PtrToStringUTF8`. **Never free.** |
| **Owned** | `wf_agent_post_result.uri`/`.cid`, `wf_response.body` | Release with the SDK's own `*_free` in a `finally`. Never let the marshaller free them. |

Do not use `[return: MarshalAs(UnmanagedType.LPUTF8Str)]` on a function
returning a borrowed pointer. The marshaller's free behaviour is
platform-dependent and will eventually free memory the SDK still owns.

### Struct layout

`WolframNative.Response` mirrors C `wf_response`. Its `status` field is a C
`long`, which is **8 bytes on Unix and 4 on Windows** — hence `CLong`, not
`long`. Using `long` silently misaligns every field after it on Windows.

`wf_response.body` is length-delimited by `body_len` and is not guaranteed to be
NUL-terminated; always read it with the two-argument
`Marshal.PtrToStringUTF8(ptr, len)`.

### Handles

`wf_agent *` is owned by `WolframAgentHandle : SafeHandle`. No raw `IntPtr`
escapes that layer. Native calls go through `WolframAgent.HandleScope`, which
`DangerousAddRef`s for the duration so the agent cannot be finalised mid-request.

### Adding an SDK call

1. Find the declaration in `wolfram/include/wolfram/*.h`.
2. Add a `[LibraryImport]` to `WolframNative` mirroring it exactly —
   `nuint` for `size_t`, `CLong` for `long`, `IntPtr` for opaque handles.
3. Add a managed method to `WolframAgent` that takes the lock, opens a
   `HandleScope`, calls it, checks the status with
   `WolframException.ThrowIfFailed`, and frees any owned output in a `finally`.
4. Expose it from `SyncService` via `Run(…)` so it lands on the thread pool.

Never add protocol logic to the wrapper. If something needs computing, it
belongs in the C SDK.

---

## 10. Threading

Godot's scene tree is **main-thread only**.

Every `libwolfram` call blocks on network I/O, so `SyncService.Run()` dispatches
to the thread pool and marshals results back with
`Callable.From(() => …).CallDeferred()`. Values are captured in the closure
rather than stashed in fields, which removes a whole class of race.

Rules:

- No `SyncService` event fires off the main thread.
- No node is touched, created, or freed off the main thread.
- `WolframAgent` serialises its own native calls with a lock; `SyncService`'s
  `SemaphoreSlim` only stops two operations racing to replace the agent.
- A failure after a successful sign-in returns to `SignedIn`, not `Failed` —
  one bad request should not log the player out.

---

## 11. Assets

**No third-party art, audio, fonts, or models. Ever.** See
[`ASSETS.md`](ASSETS.md) for the full record.

- Sound effects: synthesised by `tools/generate_assets.py`, standard library
  only, deterministic per clip. CI regenerates and fails on any diff.
- Geometry: Godot primitives built in `CourseBuilder`.
- Materials: `Palette.cs`.
- Fonts: none — the engine default is used.

Anything new must be script-generated, hand-authored as source text, or
CC0/public-domain with its provenance added to `ASSETS.md`.

---

## 12. Testing

`scenes/Smoke.tscn` → `src/Core/SmokeTest.cs`. Headless, so it runs in CI.

It loads the real `Main.tscn`, then for each course checks that it parses and
builds, that the built node counts match the data, and — the valuable part —
that the spawn, every checkpoint, and the goal are **standable**: the player is
dropped at each and must come to rest on the floor. A course whose goal hangs
over a void is valid JSON but not a finishable level, and only this catches it.

Exit code is non-zero on failure.

When adding a mechanic, prefer extending the smoke test over adding a new
harness. It is cheap and it already has a real scene running.

---

## 13. Conventions

- **C# for all runtime code** — see §2. C# 12+, nullable enabled, and
  `AllowUnsafeBlocks` (required by `LibraryImport`'s source generator).
- Godot node classes are `[GlobalClass] public partial class`. Godot's source
  generator requires `partial`.
- One public type per file, named after the file.
- XML docs on public types and non-obvious members. Comment **why**, not what —
  the code already says what.
- Prefer C# `event` over Godot signals for code-instantiated nodes: type-safe,
  no string names, no marshalling.
- Unsubscribe in `_ExitTree()` for anything subscribed in `_Ready()`.
- Use `SetDeferred` for collision-state changes; they cannot be applied during
  physics flushing.

### Commits

Conventional commits, scoped by area:

```
feat(gameplay): add wall-slide to the player controller
fix(level): correct crumble platform restore position
docs(sync): explain app password handling
```

Scopes in use: `build`, `core`, `level`, `gameplay`, `ui`, `sync`, `content`,
`assets`, `test`, `ci`, `docs`.

Commit by scope — one logical change per commit, not one commit per session.

---

## 14. Common tasks

**Add a block kind**
1. Add to `BlockKind` in `Course.cs`.
2. Give it a material in `Palette.ForBlock`.
3. Handle it in `CourseBuilder.AddBlock`.
4. If it needs behaviour on contact, prefer metadata + a check in the relevant
   system over new logic in `PlayerController`.
5. Document it in §7 and in `README.md`.

**Add a course**
Drop the JSON in `courses/`, then run the smoke test. It is discovered
automatically.

**Retune the player**
Change the exported defaults, then recompute §6 and update `README.md`. Existing
course times are not invalidated by tuning changes — only course *edits* change
the hash — so consider whether a tuning change should be paired with a course
version bump.

**Add a sync operation**
Follow §9's four steps. Keep it out of `Gameplay`.

---

## 15. Things that look wrong but are not

- **`GameInput` registering bindings in code.** Deliberate; see §5.
- **The `-2.0` grounded velocity in `PlayerController`.** Keeps `IsOnFloor()`
  stable. Removing it breaks coyote time.
- **`IsometricCamera.Yaw` returning the target, not the current, yaw.** Keeps
  held input meaningful during a rotation.
- **`Course` fully qualified as `Level.Course.Load(…)` in `GameManager`.** The
  property and the type share a name; the qualification is for the reader.
- **UI built in C# instead of `.tscn`.** See §5.
- **`native/*.dylib` gitignored.** The SDK is built from its own repo, not
  vendored.
- **"resources still in use at exit" after the smoke test.** Godot's audio
  server holds the two clips that actually played, plus their playback objects,
  past `_ExitTree`. `Sfx._ExitTree` already stops every voice, clears its
  streams and disposes them; the references that remain are engine-side
  teardown ordering, not ours. Exit code is still 0. Don't chase it.
- **`_ = RunAsync()` in `SmokeTest._Ready()`.** Godot cannot `await` in
  `_Ready`; the coroutine is intentionally fire-and-forget and ends by calling
  `GetTree().Quit()`.

---

## 16. Related

- [wolfram](https://github.com/ewanc26/wolfram) — the C11 AT Protocol SDK this
  syncs through.
- [`lexicons/uk/ewancroft/isolith/run.json`](lexicons/uk/ewancroft/isolith/run.json) — the run record schema.
